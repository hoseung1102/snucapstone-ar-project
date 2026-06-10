// EasyOCREngine — Eagle Eye v0.9.0: MLKit 대체용 EasyOCR (qai_hub_models w8a8 TFLite + QNN Delegate).
//
// 두 모델 파이프라인:
//   detector  (CRAFT):  in [1, 608, 800, 3] uint8 RGB → out [1, 304, 400, 2] uint8 (text/link score maps)
//   recognizer (CRNN):  in [1, 64, 800, 1] uint8 grey  → out [1, 199, 97]    uint8 (CTC logits, 96+blank)
//
// QnnClipEngine 와 동일한 lifecycle (initialize → recognize → release).
// QnnYoloEngine 와 동일한 양자화 입출력 처리 (uint8 input, quant params 로 dequant).
//
// 실패 시 빈 문자열 반환 — MLKitOCR 동작과 호환.

package com.eagleeye.ocr;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorMatrix;
import android.graphics.ColorMatrixColorFilter;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Rect;
import android.util.Log;

import com.qualcomm.qti.QnnDelegate;
import org.tensorflow.lite.Interpreter;

import java.io.File;
import java.io.FileInputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

public class EasyOCREngine {
    private static final String TAG = "EasyOCREngine";

    // Detector input shape (NHWC). qai-hub easyocr w8a8.
    private static final int DET_H = 608, DET_W = 800, DET_C = 3;
    // Detector output shape (NHWC) — 1/2 resolution heatmap, 2 channels (text, link).
    private static final int DET_OUT_H = 304, DET_OUT_W = 400, DET_OUT_C = 2;

    // Recognizer input shape (NHWC).
    private static final int REC_H = 64, REC_W = 800, REC_C = 1;
    // Recognizer output: [1, T=199, classes=97].
    private static final int REC_T = 199, REC_CLASSES = 97;

    // 검출 임계. 너무 낮으면 노이즈로 박스 폭발 → 인식기 부하 ↑.
    // CRAFT 원본은 text_threshold=0.7, low_text=0.4. 여기서는 quantized 라 약간 보수적.
    private static final float TEXT_THRESHOLD = 0.5f;
    private static final int MIN_BOX_AREA = 40;          // 5x8 px 이하 박스 버림
    private static final int MAX_BOXES = 32;             // 박스 너무 많으면 latency 폭발

    private Interpreter detector;
    private Interpreter recognizer;
    private QnnDelegate detDelegate, recDelegate;
    private boolean ready = false;

    // 양자화 파라미터 (initialize 에서 채움).
    private float detInScale = 1f / 255f;  private int detInZp = 0;
    private float detOutScale = 1f;        private int detOutZp = 0;
    private float recInScale = 1f / 255f;  private int recInZp = 0;
    private float recOutScale = 1f;        private int recOutZp = 0;

    // 인식기 charset (CTC blank at index 0). initialize 에서 로드.
    private char[] charset;

    public boolean initialize(String detectorPath, String recognizerPath,
                              String charsetPath, String nativeLibraryDir) {
        try {
            // --- charset 로드 (96 chars expected) ---
            File charsetFile = new File(charsetPath);
            if (charsetFile.exists()) {
                java.io.FileInputStream fis = new java.io.FileInputStream(charsetFile);
                byte[] buf = new byte[(int) charsetFile.length()];
                int read = fis.read(buf);
                fis.close();
                String csStr = new String(buf, 0, read, "UTF-8").replace("\r", "").replace("\n", "");
                charset = csStr.toCharArray();
                Log.i(TAG, "charset 로드: " + charset.length + " chars (CTC blank 미포함)");
            } else {
                Log.e(TAG, "charset 파일 없음: " + charsetPath);
                return false;
            }

            // --- detector 로드 ---
            detector = loadInterpreter(detectorPath, nativeLibraryDir, "detector");
            if (detector == null) return false;
            org.tensorflow.lite.Tensor detIn = detector.getInputTensor(0);
            org.tensorflow.lite.Tensor detOut = detector.getOutputTensor(0);
            if (detIn.quantizationParams() != null) {
                detInScale = detIn.quantizationParams().getScale();
                detInZp = detIn.quantizationParams().getZeroPoint();
            }
            if (detOut.quantizationParams() != null) {
                detOutScale = detOut.quantizationParams().getScale();
                detOutZp = detOut.quantizationParams().getZeroPoint();
            }
            int[] detInShape = detIn.shape();
            int[] detOutShape = detOut.shape();
            Log.i(TAG, "detector in=" + arr(detInShape) + " out=" + arr(detOutShape)
                + " inScale=" + detInScale + " inZp=" + detInZp
                + " outScale=" + detOutScale + " outZp=" + detOutZp);

            // --- recognizer 로드 ---
            recognizer = loadInterpreter(recognizerPath, nativeLibraryDir, "recognizer");
            if (recognizer == null) return false;
            org.tensorflow.lite.Tensor recIn = recognizer.getInputTensor(0);
            org.tensorflow.lite.Tensor recOut = recognizer.getOutputTensor(0);
            if (recIn.quantizationParams() != null) {
                recInScale = recIn.quantizationParams().getScale();
                recInZp = recIn.quantizationParams().getZeroPoint();
            }
            if (recOut.quantizationParams() != null) {
                recOutScale = recOut.quantizationParams().getScale();
                recOutZp = recOut.quantizationParams().getZeroPoint();
            }
            int[] recInShape = recIn.shape();
            int[] recOutShape = recOut.shape();
            Log.i(TAG, "recognizer in=" + arr(recInShape) + " out=" + arr(recOutShape)
                + " inScale=" + recInScale + " inZp=" + recInZp
                + " outScale=" + recOutScale + " outZp=" + recOutZp);

            // sanity check
            if (recOutShape.length != 3 || recOutShape[2] != REC_CLASSES) {
                Log.e(TAG, "recognizer output class count mismatch: " + arr(recOutShape));
                return false;
            }
            if (charset.length + 1 != REC_CLASSES) {
                Log.e(TAG, "charset(" + charset.length + ")+CTC blank != " + REC_CLASSES);
                return false;
            }

            ready = true;
            Log.i(TAG, "EasyOCR ready (NPU det=" + (detDelegate != null) + " rec=" + (recDelegate != null) + ")");
            return true;
        } catch (Throwable t) {
            Log.e(TAG, "initialize 실패: " + t.getMessage(), t);
            return false;
        }
    }

    private Interpreter loadInterpreter(String path, String nativeLibDir, String label) {
        try {
            File f = new File(path);
            if (!f.exists()) {
                Log.e(TAG, label + " tflite 파일 없음: " + path);
                return null;
            }
            FileChannel ch = new FileInputStream(f).getChannel();
            MappedByteBuffer mb = ch.map(FileChannel.MapMode.READ_ONLY, 0, ch.size());
            Log.i(TAG, label + " tflite 로드: " + path + " (" + ch.size() + " bytes)");

            QnnDelegate del = null;
            try {
                QnnDelegate.Options opts = new QnnDelegate.Options();
                opts.setBackendType(QnnDelegate.Options.BackendType.HTP_BACKEND);
                opts.setSkelLibraryDir(nativeLibDir);
                try { opts.setLogLevel(QnnDelegate.Options.LogLevel.LOG_LEVEL_INFO); } catch (Throwable ignored) {}
                try { opts.setHtpPerformanceMode(QnnDelegate.Options.HtpPerformanceMode.HTP_PERFORMANCE_BURST); } catch (Throwable ignored) {}
                del = new QnnDelegate(opts);
            } catch (Throwable t) {
                Log.e(TAG, label + " QnnDelegate 생성 실패 — CPU fallback: " + t.getMessage());
            }

            Interpreter.Options io = new Interpreter.Options();
            if (del != null) io.addDelegate(del);
            Interpreter interp = new Interpreter(mb, io);

            if ("detector".equals(label)) detDelegate = del;
            else recDelegate = del;
            return interp;
        } catch (Throwable t) {
            Log.e(TAG, label + " load 실패: " + t.getMessage(), t);
            return null;
        }
    }

    /**
     * JPG bytes → 인식된 text (모든 detected line 을 \n 으로 join). 실패 시 "".
     * rotationDegrees: 시계방향 회전 보정 (0/90/180/270).
     */
    public String recognize(byte[] jpgBytes, int rotationDegrees) {
        if (!ready) return "";

        long t0 = System.nanoTime();
        try {
            Bitmap raw = BitmapFactory.decodeByteArray(jpgBytes, 0, jpgBytes.length);
            if (raw == null) {
                Log.w(TAG, "Bitmap decode 실패");
                return "";
            }
            Bitmap bmp = rotateIfNeeded(raw, rotationDegrees);
            if (bmp != raw) raw.recycle();

            // 검출 입력: 800×608 로 letterbox.
            float[] scaleAndPad = new float[3];   // [scale, padX, padY]
            Bitmap detInBmp = letterbox(bmp, DET_W, DET_H, scaleAndPad);
            ByteBuffer detIn = bitmapToRgbUint8(detInBmp, DET_W, DET_H);
            detInBmp.recycle();

            long tDecode = System.nanoTime();

            // detector 실행 → uint8 heatmap [1, 304, 400, 2]
            byte[][][][] detOut = new byte[1][DET_OUT_H][DET_OUT_W][DET_OUT_C];
            detector.run(detIn, detOut);

            long tDet = System.nanoTime();

            // text score map dequant → float[304*400]
            float[] textMap = new float[DET_OUT_H * DET_OUT_W];
            for (int y = 0; y < DET_OUT_H; y++) {
                for (int x = 0; x < DET_OUT_W; x++) {
                    int q = detOut[0][y][x][0] & 0xFF;
                    textMap[y * DET_OUT_W + x] = (q - detOutZp) * detOutScale;
                }
            }

            // connected component → 박스 추출 (heatmap 좌표계).
            List<Rect> boxesHm = extractBoxes(textMap, DET_OUT_W, DET_OUT_H, TEXT_THRESHOLD);
            // top-to-bottom 정렬 → 자연스러운 reading order.
            Collections.sort(boxesHm, new Comparator<Rect>() {
                @Override public int compare(Rect a, Rect b) {
                    int dy = a.top - b.top;
                    if (Math.abs(dy) > 8) return dy;
                    return a.left - b.left;
                }
            });
            if (boxesHm.size() > MAX_BOXES) boxesHm = boxesHm.subList(0, MAX_BOXES);

            long tBoxes = System.nanoTime();

            // 각 박스 → letterbox 좌표 → 원본 좌표 → grayscale crop → recognizer.
            // heatmap → letterbox: ×2 (heatmap 은 letterbox 의 1/2).
            // letterbox → 원본: (x - padX) / scale.
            float scale = scaleAndPad[0], padX = scaleAndPad[1], padY = scaleAndPad[2];
            int origW = bmp.getWidth(), origH = bmp.getHeight();

            StringBuilder out = new StringBuilder();
            for (Rect hb : boxesHm) {
                // heatmap → letterbox
                int lx0 = hb.left * 2, ly0 = hb.top * 2, lx1 = hb.right * 2, ly1 = hb.bottom * 2;
                // letterbox → 원본
                int ox0 = Math.max(0, Math.round((lx0 - padX) / scale));
                int oy0 = Math.max(0, Math.round((ly0 - padY) / scale));
                int ox1 = Math.min(origW, Math.round((lx1 - padX) / scale));
                int oy1 = Math.min(origH, Math.round((ly1 - padY) / scale));
                int bw = ox1 - ox0, bh = oy1 - oy0;
                if (bw < 4 || bh < 4) continue;

                // 약간 padding (CRAFT 박스는 글자에 딱 붙음 → 여유 필요).
                int padPix = Math.max(2, bh / 6);
                ox0 = Math.max(0, ox0 - padPix);
                oy0 = Math.max(0, oy0 - padPix);
                ox1 = Math.min(origW, ox1 + padPix);
                oy1 = Math.min(origH, oy1 + padPix);
                bw = ox1 - ox0; bh = oy1 - oy0;

                Bitmap crop = Bitmap.createBitmap(bmp, ox0, oy0, bw, bh);
                String line = runRecognizer(crop);
                crop.recycle();
                if (line != null && !line.isEmpty()) {
                    if (out.length() > 0) out.append('\n');
                    out.append(line);
                }
            }
            long tRec = System.nanoTime();
            bmp.recycle();

            Log.i(TAG, String.format("TIMING ms: decode=%.1f det=%.1f boxes=%d(%.1fms) rec=%.1f total=%.1f",
                (tDecode - t0) / 1e6,
                (tDet - tDecode) / 1e6,
                boxesHm.size(),
                (tBoxes - tDet) / 1e6,
                (tRec - tBoxes) / 1e6,
                (tRec - t0) / 1e6));
            return out.toString();
        } catch (Throwable t) {
            Log.w(TAG, "recognize 실패: " + t.getMessage(), t);
            return "";
        }
    }

    // 한 박스(원본 RGB Bitmap) → 인식 텍스트.
    private String runRecognizer(Bitmap colorCrop) {
        // grayscale + letterbox into REC_W × REC_H.
        Bitmap gray = toGrayscale(colorCrop);
        Bitmap recInBmp = letterboxGray(gray, REC_W, REC_H);
        gray.recycle();

        ByteBuffer recIn = grayBitmapToUint8(recInBmp, REC_W, REC_H);
        recInBmp.recycle();

        byte[][][] recOut = new byte[1][REC_T][REC_CLASSES];
        try {
            recognizer.run(recIn, recOut);
        } catch (Throwable t) {
            Log.w(TAG, "recognizer run 실패: " + t.getMessage());
            return "";
        }

        // CTC greedy decode — 각 timestep argmax, blank(0) 제거, 연속 중복 제거.
        StringBuilder sb = new StringBuilder();
        int prev = -1;
        for (int t = 0; t < REC_T; t++) {
            int best = 0;
            int bestVal = -1;
            for (int c = 0; c < REC_CLASSES; c++) {
                int v = recOut[0][t][c] & 0xFF;
                if (v > bestVal) { bestVal = v; best = c; }
            }
            if (best != 0 && best != prev) {
                int charIdx = best - 1;   // class 0 = CTC blank
                if (charIdx >= 0 && charIdx < charset.length) sb.append(charset[charIdx]);
            }
            prev = best;
        }
        return sb.toString().trim();
    }

    // text score map 에서 binary threshold → 4-connected component → bounding boxes.
    private List<Rect> extractBoxes(float[] map, int w, int h, float threshold) {
        List<Rect> out = new ArrayList<>();
        int[] labels = new int[w * h];           // 0 = unlabeled
        int[] stack = new int[w * h];            // BFS 큐 (인덱스)
        int curLabel = 0;
        for (int i = 0; i < map.length; i++) {
            if (map[i] < threshold || labels[i] != 0) continue;
            curLabel++;
            int sp = 0;
            stack[sp++] = i;
            labels[i] = curLabel;
            int minX = i % w, maxX = minX, minY = i / w, maxY = minY;
            while (sp > 0) {
                int idx = stack[--sp];
                int x = idx % w, y = idx / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (x + 1 < w) { int n = idx + 1; if (labels[n] == 0 && map[n] >= threshold) { labels[n] = curLabel; stack[sp++] = n; } }
                if (x - 1 >= 0) { int n = idx - 1; if (labels[n] == 0 && map[n] >= threshold) { labels[n] = curLabel; stack[sp++] = n; } }
                if (y + 1 < h) { int n = idx + w; if (labels[n] == 0 && map[n] >= threshold) { labels[n] = curLabel; stack[sp++] = n; } }
                if (y - 1 >= 0) { int n = idx - w; if (labels[n] == 0 && map[n] >= threshold) { labels[n] = curLabel; stack[sp++] = n; } }
            }
            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            if (bw * bh >= MIN_BOX_AREA) out.add(new Rect(minX, minY, maxX + 1, maxY + 1));
        }
        return out;
    }

    // ---------- 이미지 헬퍼 ----------

    private Bitmap rotateIfNeeded(Bitmap src, int rotationDegrees) {
        int rot = ((rotationDegrees % 360) + 360) % 360;
        if (rot == 0) return src;
        if (rot != 90 && rot != 180 && rot != 270) return src;
        Matrix m = new Matrix();
        m.postRotate(rot);
        return Bitmap.createBitmap(src, 0, 0, src.getWidth(), src.getHeight(), m, true);
    }

    // letterbox: src 를 종횡비 유지하며 dst 안으로 fit. scaleAndPad out: [scale, padX, padY].
    private Bitmap letterbox(Bitmap src, int dstW, int dstH, float[] scaleAndPad) {
        int sw = src.getWidth(), sh = src.getHeight();
        float scale = Math.min((float) dstW / sw, (float) dstH / sh);
        int newW = Math.round(sw * scale), newH = Math.round(sh * scale);
        int padX = (dstW - newW) / 2, padY = (dstH - newH) / 2;

        Bitmap out = Bitmap.createBitmap(dstW, dstH, Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(out);
        c.drawColor(Color.BLACK);
        Rect dst = new Rect(padX, padY, padX + newW, padY + newH);
        Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG);
        c.drawBitmap(src, null, dst, paint);

        scaleAndPad[0] = scale;
        scaleAndPad[1] = padX;
        scaleAndPad[2] = padY;
        return out;
    }

    private Bitmap letterboxGray(Bitmap src, int dstW, int dstH) {
        int sw = src.getWidth(), sh = src.getHeight();
        float scale = Math.min((float) dstW / sw, (float) dstH / sh);
        int newW = Math.max(1, Math.round(sw * scale));
        int newH = Math.max(1, Math.round(sh * scale));
        int padX = (dstW - newW) / 2, padY = (dstH - newH) / 2;

        Bitmap out = Bitmap.createBitmap(dstW, dstH, Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(out);
        c.drawColor(Color.WHITE);   // EasyOCR 학습 — 배경은 흰색
        Rect dst = new Rect(padX, padY, padX + newW, padY + newH);
        Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG);
        c.drawBitmap(src, null, dst, paint);
        return out;
    }

    private Bitmap toGrayscale(Bitmap src) {
        Bitmap out = Bitmap.createBitmap(src.getWidth(), src.getHeight(), Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(out);
        ColorMatrix cm = new ColorMatrix();
        cm.setSaturation(0f);
        Paint p = new Paint();
        p.setColorFilter(new ColorMatrixColorFilter(cm));
        c.drawBitmap(src, 0, 0, p);
        return out;
    }

    // RGB Bitmap → HWC uint8 ByteBuffer (608*800*3 bytes).
    private ByteBuffer bitmapToRgbUint8(Bitmap bmp, int w, int h) {
        ByteBuffer buf = ByteBuffer.allocateDirect(w * h * 3).order(ByteOrder.nativeOrder());
        int[] px = new int[w * h];
        bmp.getPixels(px, 0, w, 0, 0, w, h);
        for (int i = 0; i < px.length; i++) {
            int p = px[i];
            buf.put((byte) ((p >> 16) & 0xFF));
            buf.put((byte) ((p >> 8) & 0xFF));
            buf.put((byte) (p & 0xFF));
        }
        buf.rewind();
        return buf;
    }

    // Gray Bitmap → HW uint8 ByteBuffer (64*800 bytes). R 채널 사용.
    private ByteBuffer grayBitmapToUint8(Bitmap bmp, int w, int h) {
        ByteBuffer buf = ByteBuffer.allocateDirect(w * h).order(ByteOrder.nativeOrder());
        int[] px = new int[w * h];
        bmp.getPixels(px, 0, w, 0, 0, w, h);
        for (int i = 0; i < px.length; i++) {
            buf.put((byte) (px[i] & 0xFF));
        }
        buf.rewind();
        return buf;
    }

    public void release() {
        try { if (detector != null) detector.close(); } catch (Throwable ignored) {}
        try { if (recognizer != null) recognizer.close(); } catch (Throwable ignored) {}
        try { if (detDelegate != null) detDelegate.close(); } catch (Throwable ignored) {}
        try { if (recDelegate != null) recDelegate.close(); } catch (Throwable ignored) {}
        detector = null; recognizer = null;
        detDelegate = null; recDelegate = null;
        ready = false;
    }

    public boolean isReady() { return ready; }

    private static String arr(int[] a) {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < a.length; i++) { if (i > 0) sb.append(","); sb.append(a[i]); }
        return sb.append("]").toString();
    }
}
