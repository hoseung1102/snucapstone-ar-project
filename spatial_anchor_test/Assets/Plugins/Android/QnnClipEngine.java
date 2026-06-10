// QnnClipEngine — MobileCLIP-S2 INT8 TFLite 추론 (TFLite + QNN Delegate)
//
// 모델 형식 (mobileclip_s2_v73_int8.tflite):
//   Input:  image  [1, 3, 256, 256] float32 (NCHW, OpenAI CLIP normalized)
//   Output: embed  [1, 512]         float32 (L2-normalized, 그래프에 포함됨)
//
// 두 가지 init 경로:
//   1) initialize(tflitePath, nativeLibDir)
//      — 기본 경로. tflite + QnnDelegate. 캐시 옵션 활성 (cache_dir + model_token).
//        첫 실행 시 ~4분 HTP 그래프 컴파일 후 캐시 파일 생성. 이후 launch 는 캐시 hit 로 즉시 로드.
//   2) initializeFromContextBin(binPath, nativeLibDir)
//      — 사전 빌드된 QNN HTP context cache 파일을 미리 cache_dir 에 심고 1) 과 동일 경로 호출.
//        StreamingAssets 에 `mobileclip_s2.qnn_context.bin` 동봉 시 사용. 없으면 호출 안 함.
//
// 양 경로 모두 동일한 `embed(float[])` 인터페이스. 캐시 / 비캐시 차이는 init latency 뿐.
//
// Hexagon DSP 보안 구조상 root 없는 3rd-party 앱은 libQnnHtp.so 직접 호출 불가
// (`docs/dev-guide.md` §4 / vision.md "QNN direct → LiteRT 피벗" 결정 로그).
// 따라서 "context binary 로 직접 로드" 는 QnnDelegate 의 setCacheDir/setModelToken 기반
// HTP context cache 가 유일한 합법 경로다. AAR 2.47.0 의 javap 으로 확인된 public API.

package com.eagleeye.qnn;

import org.tensorflow.lite.Interpreter;
import com.qualcomm.qti.QnnDelegate;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.MappedByteBuffer;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.nio.channels.FileChannel;
import android.util.Log;

public class QnnClipEngine {
    private static final String TAG = "QnnClipEngine";

    // HTP context cache 의 namespace. tflite 모델 graph hash 와 1:1.
    // 모델 파일이 바뀌면 토큰도 바꿔야 stale cache 안 씀.
    private static final String CACHE_MODEL_TOKEN = "mobileclip_s2_v73_int8_v1";
    // cache_dir 안에 들어갈 사전 빌드 캐시 파일 이름. (StreamingAssets 에서 복사할 때 이 이름으로.)
    // QnnDelegate 가 캐시 파일을 `<cache_dir>/<model_token>.bin` 으로 쓰므로 동일 규약 따름.
    private static final String CACHE_BIN_FILENAME = CACHE_MODEL_TOKEN + ".bin";

    private Interpreter interpreter;
    private QnnDelegate delegate;
    private boolean ready = false;
    private boolean mockMode = false;
    private boolean cacheBinPrewarmed = false;

    public static final int IN_C = 3, IN_H = 256, IN_W = 256;
    public static final int EMBED_DIM = 512;
    public static final int INPUT_FLOATS = IN_C * IN_H * IN_W;   // 196,608

    /**
     * 기본 init 경로. tflite + QnnDelegate, HTP cache 활성.
     *   첫 실행: ~4분 컴파일 후 cache_dir 에 캐시 파일 기록
     *   이후:     캐시 hit 으로 즉시 로드 (~수백 ms)
     */
    public boolean initialize(String tfliteModelPath, String nativeLibraryDir) {
        return initInternal(tfliteModelPath, nativeLibraryDir, null);
    }

    /**
     * Pre-compiled HTP context cache 사전 심기 경로.
     *   binPath 가 가리키는 사전 빌드 캐시 바이너리(StreamingAssets 동봉 등)를
     *   QnnDelegate cache_dir/<model_token>.bin 위치로 복사한 뒤,
     *   기존 initialize 와 동일한 tflite + delegate 경로로 init.
     *   delegate 가 캐시 파일 발견 → HTP compile skip → 즉시 ready.
     *
     *   tflitePath 는 같은 모델의 tflite 가 여전히 필요 (graph topology + tensor shape 메타데이터용).
     *   이 메소드는 tflite 옆에 사전 빌드 .bin 도 같이 있는 케이스 전용.
     *   tflite 만 있고 .bin 이 없으면 호출하지 말고 initialize() 직접 호출할 것.
     */
    public boolean initializeFromContextBin(String binPath, String tflitePath, String nativeLibraryDir) {
        try {
            File binFile = new File(binPath);
            if (!binFile.exists() || binFile.length() == 0) {
                Log.e(TAG, "사전 빌드 context bin 없음/비어있음: " + binPath + " — initialize() 폴백 권장");
                return initInternal(tflitePath, nativeLibraryDir, null);
            }
            Log.i(TAG, "사전 빌드 HTP cache 발견: " + binPath + " (" + binFile.length() + " bytes)");
            return initInternal(tflitePath, nativeLibraryDir, binFile);
        } catch (Throwable t) {
            Log.e(TAG, "initializeFromContextBin 예외: " + t.getMessage(), t);
            return false;
        }
    }

    private boolean initInternal(String tfliteModelPath, String nativeLibraryDir, File prebuiltCacheBin) {
        try {
            File modelFile = new File(tfliteModelPath);
            if (!modelFile.exists()) {
                Log.e(TAG, "tflite 파일 없음: " + tfliteModelPath);
                mockMode = true;
                ready = true;
                return true;
            }

            // QnnDelegate HTP cache 디렉토리 — 앱 cache 영역 안에 격리.
            // /storage/emulated/0/Android/data/<pkg>/cache/qnn_clip_cache/
            // tflite 파일은 persistentDataPath 에 있으므로 그 옆에 cache 디렉토리 둠.
            File parentDir = modelFile.getParentFile();
            File cacheDir = new File(parentDir, "qnn_clip_cache");
            if (!cacheDir.exists()) {
                boolean made = cacheDir.mkdirs();
                Log.i(TAG, "HTP cache 디렉토리 생성: " + cacheDir.getAbsolutePath() + " ok=" + made);
            }

            // 사전 빌드 캐시 바이너리가 제공되면 cache_dir/<model_token>.bin 으로 복사.
            // delegate 가 그 이름의 파일을 찾으면 컴파일을 건너뛰고 deserialize 한다.
            if (prebuiltCacheBin != null) {
                File targetCacheBin = new File(cacheDir, CACHE_BIN_FILENAME);
                if (!targetCacheBin.exists() || targetCacheBin.length() != prebuiltCacheBin.length()) {
                    copyFile(prebuiltCacheBin, targetCacheBin);
                    Log.i(TAG, "사전 빌드 cache 복사 완료: " + targetCacheBin.getAbsolutePath() + " (" + targetCacheBin.length() + " bytes)");
                    cacheBinPrewarmed = true;
                } else {
                    Log.i(TAG, "사전 빌드 cache 이미 존재 (skip copy): " + targetCacheBin.getAbsolutePath());
                    cacheBinPrewarmed = true;
                }
            }

            FileChannel channel = new FileInputStream(modelFile).getChannel();
            MappedByteBuffer modelBuffer = channel.map(FileChannel.MapMode.READ_ONLY, 0, channel.size());
            Log.i(TAG, "tflite 모델 로드: " + tfliteModelPath + " (" + channel.size() + " bytes)");

            try {
                QnnDelegate.Options opts = new QnnDelegate.Options();
                opts.setBackendType(QnnDelegate.Options.BackendType.HTP_BACKEND);
                opts.setSkelLibraryDir(nativeLibraryDir);
                // HTP context cache 활성. (cache_dir + model_token 둘 다 set 해야 동작.)
                opts.setCacheDir(cacheDir.getAbsolutePath());
                opts.setModelToken(CACHE_MODEL_TOKEN);
                try { opts.setLogLevel(QnnDelegate.Options.LogLevel.LOG_LEVEL_INFO); } catch (Throwable t1) {}
                try { opts.setHtpPerformanceMode(QnnDelegate.Options.HtpPerformanceMode.HTP_PERFORMANCE_BURST); } catch (Throwable t1) {}
                delegate = new QnnDelegate(opts);
                Log.i(TAG, "QnnDelegate (HTP) 생성됨 cacheDir=" + cacheDir.getAbsolutePath()
                    + " token=" + CACHE_MODEL_TOKEN
                    + " prewarmed=" + cacheBinPrewarmed);
            } catch (Throwable t) {
                Log.e(TAG, "QnnDelegate 생성 실패 — CPU fallback: " + t.getMessage());
                delegate = null;
            }

            long tBeforeInterp = System.nanoTime();
            Interpreter.Options interpOpts = new Interpreter.Options();
            if (delegate != null) interpOpts.addDelegate(delegate);
            interpreter = new Interpreter(modelBuffer, interpOpts);
            long tAfterInterp = System.nanoTime();
            Log.i(TAG, String.format("Interpreter init: %.1f ms (캐시 hit 이면 짧음, miss 면 수 분)",
                (tAfterInterp - tBeforeInterp) / 1e6));

            // 모델 input/output spec 검증
            int[] inShape = interpreter.getInputTensor(0).shape();
            int[] outShape = interpreter.getOutputTensor(0).shape();
            Log.i(TAG, "✅ CLIP Interpreter ready NPU=" + (delegate != null)
                + " inShape=" + arrayToString(inShape) + " outShape=" + arrayToString(outShape));

            // sanity check
            if (inShape.length != 4 || inShape[1] != IN_C || inShape[2] != IN_H || inShape[3] != IN_W) {
                Log.e(TAG, "❌ 예상 입력 shape (1,3,256,256) 과 다름: " + arrayToString(inShape));
                return false;
            }
            if (outShape.length != 2 || outShape[1] != EMBED_DIM) {
                Log.e(TAG, "❌ 예상 출력 shape (1,512) 과 다름: " + arrayToString(outShape));
                return false;
            }

            // 캐시 hit 여부 추정 — Interpreter ctor 가 1초 이하면 hit, 그 이상이면 miss(컴파일).
            // 정확한 hit/miss API 는 delegate 가 노출 안 함. 측정값으로만 판단.
            File expectedCache = new File(cacheDir, CACHE_BIN_FILENAME);
            Log.i(TAG, "cache 파일 상태 after init: exists=" + expectedCache.exists()
                + " size=" + (expectedCache.exists() ? expectedCache.length() : -1));

            ready = true;
            return true;

        } catch (Throwable e) {
            Log.e(TAG, "init 실패: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }

    /**
     * 이미지 (전처리 완료된 NCHW float32) → 512차원 임베딩.
     * 입력 형식: input[0..196607] = 채널순 [R[256*256], G[256*256], B[256*256]] OpenAI CLIP normalized.
     */
    public float[] embed(float[] input) {
        if (!ready) return new float[EMBED_DIM];
        if (input.length != INPUT_FLOATS) {
            Log.e(TAG, "입력 길이 불일치: " + input.length + " vs " + INPUT_FLOATS);
            return new float[EMBED_DIM];
        }

        if (mockMode) {
            // Mock — 입력 hash 기반 의사 임베딩 (테스트용)
            float[] mock = new float[EMBED_DIM];
            int seed = java.util.Arrays.hashCode(input);
            for (int i = 0; i < EMBED_DIM; i++) mock[i] = ((seed + i * 31) % 100) / 100f;
            // L2 normalize
            float n = 0f;
            for (float v : mock) n += v * v;
            n = (float) Math.sqrt(n);
            if (n > 0) for (int i = 0; i < EMBED_DIM; i++) mock[i] /= n;
            return mock;
        }

        ByteBuffer inBuf = ByteBuffer.allocateDirect(INPUT_FLOATS * 4).order(ByteOrder.nativeOrder());
        FloatBuffer inFloat = inBuf.asFloatBuffer();
        inFloat.put(input);

        float[][] out = new float[1][EMBED_DIM];
        try {
            interpreter.run(inBuf, out);
        } catch (Throwable t) {
            Log.e(TAG, "embed 실패: " + t.getMessage());
            return new float[EMBED_DIM];
        }

        return out[0];   // (512,)
    }

    public void release() {
        if (interpreter != null) { interpreter.close(); interpreter = null; }
        if (delegate != null) { delegate.close(); delegate = null; }
        ready = false;
    }

    public boolean isMockMode() { return mockMode; }
    public boolean isReady() { return ready; }
    public boolean isCacheBinPrewarmed() { return cacheBinPrewarmed; }

    private static String arrayToString(int[] a) {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < a.length; i++) { if (i > 0) sb.append(","); sb.append(a[i]); }
        return sb.append("]").toString();
    }

    private static void copyFile(File src, File dst) throws java.io.IOException {
        try (InputStream in = new FileInputStream(src);
             OutputStream out = new FileOutputStream(dst)) {
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        }
    }
}
