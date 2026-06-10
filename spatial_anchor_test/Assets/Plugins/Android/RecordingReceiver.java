package com.eagleeye.rec;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Build;
import android.util.Log;

// RecordingReceiver — adb broadcast 로 1인칭 시야 녹화를 토글하는 dynamic receiver.
// FirstPersonRecorder.cs 가 register() 호출 후 isRecording() 을 polling.
//
// 트리거 (앱이 foreground 일 때만 수신됨):
//   adb shell am broadcast -a com.eagleeye.RECORD_START [--es session <이름>]
//   adb shell am broadcast -a com.eagleeye.RECORD_STOP
//
// Android 13+ 는 dynamic receiver 에 RECEIVER_EXPORTED 명시 필수
// (adb shell broadcast 는 외부 uid 라 exported 아니면 수신 불가).
public class RecordingReceiver extends BroadcastReceiver {
    private static final String TAG = "EagleEyeRec";
    public static final String ACTION_START = "com.eagleeye.RECORD_START";
    public static final String ACTION_STOP  = "com.eagleeye.RECORD_STOP";

    private static volatile boolean sRecording = false;
    private static volatile String sSessionId = "";
    private static RecordingReceiver sInstance;

    /** Unity 쪽에서 1회 호출. activity = UnityPlayer.currentActivity */
    public static synchronized void register(Activity activity) {
        if (sInstance != null) return;
        sInstance = new RecordingReceiver();
        IntentFilter filter = new IntentFilter();
        filter.addAction(ACTION_START);
        filter.addAction(ACTION_STOP);
        if (Build.VERSION.SDK_INT >= 33) {
            activity.registerReceiver(sInstance, filter, Context.RECEIVER_EXPORTED);
        } else {
            activity.registerReceiver(sInstance, filter);
        }
        Log.i(TAG, "RecordingReceiver registered (exported)");
    }

    public static boolean isRecording() { return sRecording; }
    public static String getSessionId() { return sSessionId; }

    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent.getAction();
        if (ACTION_START.equals(action)) {
            String s = intent.getStringExtra("session");
            sSessionId = (s != null && !s.isEmpty())
                    ? s : ("session_" + System.currentTimeMillis());
            sRecording = true;
            Log.i(TAG, "RECORD_START session=" + sSessionId);
        } else if (ACTION_STOP.equals(action)) {
            sRecording = false;
            Log.i(TAG, "RECORD_STOP session=" + sSessionId);
        }
    }
}
