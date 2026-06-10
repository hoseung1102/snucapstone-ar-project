// QnnYoloEngine — v0.3.0: 공식 qai_hub_models YOLOv11-Det 모델 사용
//
// 모델 형식 (qai_hub_models w8a8 또는 float):
//   Input:  image [1, 320, 320, 3] uint8 (양자화) or float32 (float)
//   Output 1: boxes     [1, 2100, 4]   양자화 dtype, scale 적용 후 픽셀 좌표
//   Output 2: scores    [1, 2100]      scale=1/256 → [0,1] confidence
//   Output 3: class_idx [1, 2100]      uint8 raw → 0~79 class index
//
// NPU 가 DFL + sigmoid + argmax 까지 다 처리. C# 는 threshold + NMS 만.

package com.eagleeye.qnn;

import org.tensorflow.lite.Interpreter;
import com.qualcomm.qti.QnnDelegate;

import java.io.File;
import java.io.FileInputStream;
import java.nio.MappedByteBuffer;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.FileChannel;
import java.util.HashMap;
import java.util.Map;
import android.util.Log;

public class QnnYoloEngine {
    private static final String TAG = "QnnYoloEngine";

    private Interpreter interpreter;
    private QnnDelegate delegate;
    private boolean ready = false;
    private boolean mockMode = false;

    private static final int IN_H = 640, IN_W = 640, IN_C = 3;
    private static final int NUM_ANCHORS = 8400;  // 80² + 40² + 20² (640 input)

    // 양자화/비양자화 모델 자동 감지
    private boolean isQuantized = false;        // input dtype 으로 결정
    private float boxScale = 1.0f;
    private int boxZeroPoint = 0;
    private float scoreScale = 1.0f;
    private int scoreZeroPoint = 0;
    private int boxOutIdx = -1, scoreOutIdx = -1, classOutIdx = -1;

    // v0.3.2 Option C: reusable direct buffer + native address 노출
    private java.nio.ByteBuffer reusableInputBuffer;
    private static final int INPUT_BYTES = IN_H * IN_W * IN_C;  // 640*640*3 = 1,228,800

    // 한 번 alloc + native address 반환. C# 가 그 주소에 직접 byte 쓰기.
    // reflection 으로 java.nio.Buffer.address field 접근. Android 9+ 에서 hidden API warning 가능.
    public long allocateInputBuffer() {
        try {
            if (reusableInputBuffer == null) {
                reusableInputBuffer = java.nio.ByteBuffer.allocateDirect(INPUT_BYTES)
                    .order(java.nio.ByteOrder.nativeOrder());
            }
            // address field 접근
            java.lang.reflect.Field f = java.nio.Buffer.class.getDeclaredField("address");
            f.setAccessible(true);
            long addr = f.getLong(reusableInputBuffer);
            Log.i(TAG, "Allocated reusable direct buffer: size=" + INPUT_BYTES + " addr=0x" + Long.toHexString(addr));
            return addr;
        } catch (Throwable t) {
            Log.e(TAG, "allocateInputBuffer 실패: " + t.getMessage());
            return 0;
        }
    }

    public boolean initialize(String tfliteModelPath, String nativeLibraryDir) {
        try {
            File modelFile = new File(tfliteModelPath);
            if (!modelFile.exists()) {
                Log.e(TAG, "tflite 파일 없음: " + tfliteModelPath);
                mockMode = true;
                return true;
            }

            FileChannel channel = new FileInputStream(modelFile).getChannel();
            MappedByteBuffer modelBuffer = channel.map(FileChannel.MapMode.READ_ONLY, 0, channel.size());
            Log.i(TAG, "tflite 모델 로드: " + tfliteModelPath + " (" + channel.size() + " bytes)");

            try {
                QnnDelegate.Options opts = new QnnDelegate.Options();
                opts.setBackendType(QnnDelegate.Options.BackendType.HTP_BACKEND);
                opts.setSkelLibraryDir(nativeLibraryDir);
                try { opts.setLogLevel(QnnDelegate.Options.LogLevel.LOG_LEVEL_INFO); } catch (Throwable t1) {}
                try { opts.setHtpPerformanceMode(QnnDelegate.Options.HtpPerformanceMode.HTP_PERFORMANCE_BURST); } catch (Throwable t1) {}
                delegate = new QnnDelegate(opts);
                Log.i(TAG, "QnnDelegate (HTP) 생성됨");
            } catch (Throwable t) {
                Log.e(TAG, "QnnDelegate 생성 실패 — CPU fallback: " + t.getMessage());
                delegate = null;
            }

            Interpreter.Options interpOpts = new Interpreter.Options();
            if (delegate != null) interpOpts.addDelegate(delegate);
            interpreter = new Interpreter(modelBuffer, interpOpts);

            // 모델 형식 판별
            int inDtype = interpreter.getInputTensor(0).dataType().byteSize();
            isQuantized = (inDtype == 1);  // uint8 (1byte) → 양자화 / float32 (4byte) → float

            int numOutputs = interpreter.getOutputTensorCount();
            StringBuilder log = new StringBuilder();
            for (int i = 0; i < numOutputs; i++) {
                int[] s = interpreter.getOutputTensor(i).shape();
                String name = interpreter.getOutputTensor(i).name();
                log.append(" out").append(i).append("='").append(name).append("' shape=");
                for (int v : s) log.append(v).append(",");

                // shape 으로 자동 분류
                // boxes [1, 2100, 4], scores [1, 2100], class_idx [1, 2100]
                if (s.length == 3 && s[1] == NUM_ANCHORS && s[2] == 4) {
                    boxOutIdx = i;
                    org.tensorflow.lite.Tensor t = interpreter.getOutputTensor(i);
                    if (t.quantizationParams() != null) {
                        boxScale = t.quantizationParams().getScale();
                        boxZeroPoint = t.quantizationParams().getZeroPoint();
                    }
                } else if (s.length == 2 && s[1] == NUM_ANCHORS) {
                    // scores vs class_idx — name 으로 구분, 없으면 scale 로 (scores 는 scale > 0)
                    org.tensorflow.lite.Tensor t = interpreter.getOutputTensor(i);
                    float sc = (t.quantizationParams() != null) ? t.quantizationParams().getScale() : 0f;
                    int zp = (t.quantizationParams() != null) ? t.quantizationParams().getZeroPoint() : 0;
                    if (name.contains("class") || (sc == 0f && isQuantized)) {
                        classOutIdx = i;
                    } else {
                        scoreOutIdx = i;
                        scoreScale = sc;
                        scoreZeroPoint = zp;
                    }
                }
            }
            Log.i(TAG, "✅ Interpreter ready NPU=" + (delegate != null)
                + " quantized=" + isQuantized
                + " boxIdx=" + boxOutIdx + " boxScale=" + boxScale + " boxZp=" + boxZeroPoint
                + " scoreIdx=" + scoreOutIdx + " scoreScale=" + scoreScale + " scoreZp=" + scoreZeroPoint
                + " classIdx=" + classOutIdx
                + log);

            ready = true;
            return true;
        } catch (Throwable t) {
            Log.e(TAG, "initialize 예외: " + t.getMessage(), t);
            mockMode = true;
            return true;
        }
    }

    // input: flat float[320*320*3] in [0,1] (Unity Texture readback)
    // 결과: float[] [boxes(2100*4) | scores(2100) | class_idx(2100)] = 14700 length
    //   layout: [box0_cx, box0_cy, box0_w, box0_h, box1_cx, ..., score0, score1, ..., class0, class1, ...]
    public float[] execute(float[] input) {
        if (mockMode) return new float[0];
        if (!ready || interpreter == null) return new float[0];
        if (boxOutIdx < 0 || scoreOutIdx < 0 || classOutIdx < 0) {
            Log.e(TAG, "output indices not resolved");
            return new float[0];
        }

        long tEntry = System.nanoTime();
        try {
            // input 변환: float[0,1] NCHW → 모델 input layout
            Object inputTensor;
            if (isQuantized) {
                // NHWC uint8 [1, 320, 320, 3]
                byte[][][][] in4 = new byte[1][IN_H][IN_W][IN_C];
                // input flat 는 NCHW float[0,1] → NHWC uint8[0,255]
                int plane = IN_H * IN_W;
                for (int h = 0; h < IN_H; h++)
                    for (int w = 0; w < IN_W; w++) {
                        int pos = h * IN_W + w;
                        in4[0][h][w][0] = (byte) Math.min(255, Math.max(0, (int)(input[0 * plane + pos] * 255f)));
                        in4[0][h][w][1] = (byte) Math.min(255, Math.max(0, (int)(input[1 * plane + pos] * 255f)));
                        in4[0][h][w][2] = (byte) Math.min(255, Math.max(0, (int)(input[2 * plane + pos] * 255f)));
                    }
                inputTensor = in4;
            } else {
                // NHWC float32 [1, 320, 320, 3]
                float[][][][] in4 = new float[1][IN_H][IN_W][IN_C];
                int plane = IN_H * IN_W;
                for (int h = 0; h < IN_H; h++)
                    for (int w = 0; w < IN_W; w++) {
                        int pos = h * IN_W + w;
                        in4[0][h][w][0] = input[0 * plane + pos];
                        in4[0][h][w][1] = input[1 * plane + pos];
                        in4[0][h][w][2] = input[2 * plane + pos];
                    }
                inputTensor = in4;
            }
            long tAfterInputConvert = System.nanoTime();

            // outputs 준비 — 각 output 의 dtype 으로 결정 (float 모델도 class_idx 는 uint8)
            int boxBytes   = interpreter.getOutputTensor(boxOutIdx).dataType().byteSize();
            int scoreBytes = interpreter.getOutputTensor(scoreOutIdx).dataType().byteSize();
            int classBytes = interpreter.getOutputTensor(classOutIdx).dataType().byteSize();
            Map<Integer, Object> outMap = new HashMap<>();
            Object boxOut = (boxBytes == 1) ? (Object) new byte[1][NUM_ANCHORS][4] : (Object) new float[1][NUM_ANCHORS][4];
            Object scoreOut = (scoreBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            Object classOut = (classBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            outMap.put(boxOutIdx,   boxOut);
            outMap.put(scoreOutIdx, scoreOut);
            outMap.put(classOutIdx, classOut);

            long tBeforeInvoke = System.nanoTime();
            interpreter.runForMultipleInputsOutputs(new Object[]{inputTensor}, outMap);
            long tAfterInvoke = System.nanoTime();

            // dequant + flatten — 각 output 의 dtype 따라
            float[] flat = new float[NUM_ANCHORS * 4 + NUM_ANCHORS + NUM_ANCHORS];
            int wp = 0;

            if (boxBytes == 1) {
                byte[][][] b = (byte[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = ((b[0][a][k] & 0xFF) - boxZeroPoint) * boxScale;
            } else {
                float[][][] b = (float[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = b[0][a][k];
            }
            if (scoreBytes == 1) {
                byte[][] s = (byte[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = ((s[0][a] & 0xFF) - scoreZeroPoint) * scoreScale;
            } else {
                float[][] s = (float[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = s[0][a];
            }
            if (classBytes == 1) {
                byte[][] c = (byte[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = (float)(c[0][a] & 0xFF);
            } else {
                float[][] c = (float[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = c[0][a];
            }
            long tAfterDequant = System.nanoTime();
            Log.i(TAG, String.format("TIMING ms: inputConvert=%.1f invoke=%.1f dequant=%.1f total=%.1f",
                (tAfterInputConvert - tEntry) / 1e6,
                (tAfterInvoke - tBeforeInvoke) / 1e6,
                (tAfterDequant - tAfterInvoke) / 1e6,
                (tAfterDequant - tEntry) / 1e6));
            return flat;
        } catch (Throwable t) {
            Log.e(TAG, "execute 예외: " + t.getMessage());
            return new float[0];
        }
    }

    // v0.3.1: C# 에서 NHWC uint8 byte[] 를 직접 받음 → Java loop 제거
    // input: byte[320*320*3] in NHWC uint8 layout
    public float[] executeBytes(byte[] input) {
        if (mockMode) return new float[0];
        if (!ready || interpreter == null) return new float[0];
        if (boxOutIdx < 0 || scoreOutIdx < 0 || classOutIdx < 0) return new float[0];

        long tEntry = System.nanoTime();
        try {
            java.nio.ByteBuffer inBuf = java.nio.ByteBuffer.allocateDirect(input.length).order(java.nio.ByteOrder.nativeOrder());
            inBuf.put(input);
            inBuf.rewind();
            long tAfterWrap = System.nanoTime();

            int boxBytes   = interpreter.getOutputTensor(boxOutIdx).dataType().byteSize();
            int scoreBytes = interpreter.getOutputTensor(scoreOutIdx).dataType().byteSize();
            int classBytes = interpreter.getOutputTensor(classOutIdx).dataType().byteSize();
            Map<Integer, Object> outMap = new HashMap<>();
            Object boxOut = (boxBytes == 1) ? (Object) new byte[1][NUM_ANCHORS][4] : (Object) new float[1][NUM_ANCHORS][4];
            Object scoreOut = (scoreBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            Object classOut = (classBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            outMap.put(boxOutIdx,   boxOut);
            outMap.put(scoreOutIdx, scoreOut);
            outMap.put(classOutIdx, classOut);

            long tBeforeInvoke = System.nanoTime();
            interpreter.runForMultipleInputsOutputs(new Object[]{inBuf}, outMap);
            long tAfterInvoke = System.nanoTime();

            float[] flat = new float[NUM_ANCHORS * 4 + NUM_ANCHORS + NUM_ANCHORS];
            int wp = 0;
            if (boxBytes == 1) {
                byte[][][] b = (byte[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = ((b[0][a][k] & 0xFF) - boxZeroPoint) * boxScale;
            } else {
                float[][][] b = (float[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = b[0][a][k];
            }
            if (scoreBytes == 1) {
                byte[][] s = (byte[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = ((s[0][a] & 0xFF) - scoreZeroPoint) * scoreScale;
            } else {
                float[][] s = (float[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = s[0][a];
            }
            if (classBytes == 1) {
                byte[][] c = (byte[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = (float)(c[0][a] & 0xFF);
            } else {
                float[][] c = (float[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = c[0][a];
            }
            long tEnd = System.nanoTime();
            Log.i(TAG, String.format("TIMING ms: wrap=%.1f invoke=%.1f dequant=%.1f total=%.1f",
                (tAfterWrap - tEntry) / 1e6,
                (tAfterInvoke - tBeforeInvoke) / 1e6,
                (tEnd - tAfterInvoke) / 1e6,
                (tEnd - tEntry) / 1e6));
            return flat;
        } catch (Throwable t) {
            Log.e(TAG, "executeBytes 예외: " + t.getMessage());
            return new float[0];
        }
    }

    // BENCH: noop call — byte[] in + tiny float[] out 만. invoke 와 dequant 없음.
    public float[] executeNoop(byte[] input) {
        return new float[8];
    }

    // v0.3.2 Option C: input 인자 없음. reusableInputBuffer 가 이미 C# 에서 채워졌다고 가정.
    public float[] executeReusable() {
        if (mockMode) return new float[0];
        if (!ready || interpreter == null) return new float[0];
        if (reusableInputBuffer == null) return new float[0];
        if (boxOutIdx < 0 || scoreOutIdx < 0 || classOutIdx < 0) return new float[0];

        long tEntry = System.nanoTime();
        try {
            reusableInputBuffer.rewind();

            int boxBytes   = interpreter.getOutputTensor(boxOutIdx).dataType().byteSize();
            int scoreBytes = interpreter.getOutputTensor(scoreOutIdx).dataType().byteSize();
            int classBytes = interpreter.getOutputTensor(classOutIdx).dataType().byteSize();
            Map<Integer, Object> outMap = new HashMap<>();
            Object boxOut = (boxBytes == 1) ? (Object) new byte[1][NUM_ANCHORS][4] : (Object) new float[1][NUM_ANCHORS][4];
            Object scoreOut = (scoreBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            Object classOut = (classBytes == 1) ? (Object) new byte[1][NUM_ANCHORS] : (Object) new float[1][NUM_ANCHORS];
            outMap.put(boxOutIdx,   boxOut);
            outMap.put(scoreOutIdx, scoreOut);
            outMap.put(classOutIdx, classOut);

            long tBeforeInvoke = System.nanoTime();
            interpreter.runForMultipleInputsOutputs(new Object[]{reusableInputBuffer}, outMap);
            long tAfterInvoke = System.nanoTime();

            float[] flat = new float[NUM_ANCHORS * 4 + NUM_ANCHORS + NUM_ANCHORS];
            int wp = 0;
            if (boxBytes == 1) {
                byte[][][] b = (byte[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = ((b[0][a][k] & 0xFF) - boxZeroPoint) * boxScale;
            } else {
                float[][][] b = (float[][][]) boxOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    for (int k = 0; k < 4; k++)
                        flat[wp++] = b[0][a][k];
            }
            if (scoreBytes == 1) {
                byte[][] s = (byte[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = ((s[0][a] & 0xFF) - scoreZeroPoint) * scoreScale;
            } else {
                float[][] s = (float[][]) scoreOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = s[0][a];
            }
            if (classBytes == 1) {
                byte[][] c = (byte[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = (float)(c[0][a] & 0xFF);
            } else {
                float[][] c = (float[][]) classOut;
                for (int a = 0; a < NUM_ANCHORS; a++)
                    flat[wp++] = c[0][a];
            }
            long tEnd = System.nanoTime();
            Log.i(TAG, String.format("REUSABLE TIMING ms: invoke=%.1f dequant=%.1f total=%.1f",
                (tAfterInvoke - tBeforeInvoke) / 1e6,
                (tEnd - tAfterInvoke) / 1e6,
                (tEnd - tEntry) / 1e6));
            return flat;
        } catch (Throwable t) {
            Log.e(TAG, "executeReusable 예외: " + t.getMessage());
            return new float[0];
        }
    }

    public void release() {
        if (interpreter != null) { interpreter.close(); interpreter = null; }
        if (delegate != null)    { delegate.close();    delegate = null; }
        ready = false;
    }

    public boolean isMockMode() { return mockMode; }
}
