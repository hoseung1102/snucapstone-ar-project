using System;
using UnityEngine;

// IMU 자이로 기반 "시선 안정" 트리거.
// 기획안 v1.2 Step 1의 Android 구현. 맥북 PoC의 Optical Flow 트리거를 대체.
//
// 조건: |gx|, |gy|, |gz| 모두 stableThreshold 이하로 stableDuration 이상 유지
// 발화: OnTrigger event invoke + 쿨다운 후 재발화 가능
//
// 안경 RayNeo X3 Pro 센서 정보 (2026-06-06 adb 검증):
//   - lsm6dsr STMicro 6축 IMU
//   - android.sensor.gyroscope (type=4), 단위 rad/s
//   - 최대 415.97Hz, 기본 사용은 50Hz (com.probe.imu와 동일)
//
// 단위 주의: 1 rad/s ≈ 57.3 deg/s. 임계값 1.0은 관대한 편 — 실측 후 튜닝 권장.
public class GyroTrigger : MonoBehaviour
{
    [Header("트리거 파라미터")]
    [Tooltip("각 축 절대값이 이 값 이하여야 '안정'으로 간주 (rad/s). 0.3 rad/s ≈ 17°/s")]
    public float stableThreshold = 0.3f;

    [Tooltip("안정 상태가 이만큼 지속되면 트리거 발화 (초)")]
    public float stableDuration = 2.0f;

    [Tooltip("한 번 발화 후 안정이 깨지기 전까지는 재발화 안 함. 이 옵션이 true면 cooldown 무시")]
    public bool oneShotPerStableWindow = true;

    [Tooltip("oneShotPerStableWindow=false 일 때만 적용. 트리거 발화 후 재발화 가능까지 쿨다운 (초)")]
    public float triggerCooldown = 1.0f;

    [Tooltip("Unity 자이로 폴링 주기 (초). 0.02 = 50Hz")]
    public float gyroUpdateInterval = 0.02f;

    [Header("디버그 / 모니터링 (런타임 노출)")]
    [Tooltip("매 프레임 자이로 값 콘솔 출력 (성능 영향 ↑)")]
    public bool verboseLog = false;

    public Vector3 currentGyro;        // 최근 자이로 값 (rad/s)
    public float currentMaxAbs;        // 3축 중 최대 절대값
    public bool isStable;              // 현재 안정 상태
    public float stableElapsed;        // 현재 안정 지속 시간 (초)
    public int totalTriggers;          // 누적 트리거 횟수
    public float lastTriggerTime = -1f;

    public event Action OnTrigger;

    float stableStartTime = -1f;
    bool firedInThisStableWindow = false;

    void Start()
    {
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            Input.gyro.updateInterval = gyroUpdateInterval;
            Debug.Log($"[GyroTrigger] Init OK. threshold={stableThreshold} rad/s, duration={stableDuration}s, cooldown={triggerCooldown}s, rate={1f/gyroUpdateInterval}Hz");
        }
        else
        {
            Debug.LogError("[GyroTrigger] Device does not support gyroscope! Disabled.");
            enabled = false;
        }
    }

    void Update()
    {
        Vector3 g = Input.gyro.rotationRateUnbiased;  // rad/s, bias 제거된 값
        currentGyro = g;
        currentMaxAbs = Mathf.Max(Mathf.Abs(g.x), Mathf.Max(Mathf.Abs(g.y), Mathf.Abs(g.z)));

        bool nowStable = currentMaxAbs <= stableThreshold;
        isStable = nowStable;

        if (verboseLog)
        {
            Debug.Log($"[GyroTrigger] g=({g.x:F3},{g.y:F3},{g.z:F3}) maxAbs={currentMaxAbs:F3} stable={nowStable}");
        }

        if (nowStable)
        {
            if (stableStartTime < 0f) stableStartTime = Time.time;
            stableElapsed = Time.time - stableStartTime;

            bool durationMet = stableElapsed >= stableDuration;
            bool allowedToFire;
            if (oneShotPerStableWindow)
            {
                // 이 안정 구간에서 아직 발화 안 했어야 함
                allowedToFire = !firedInThisStableWindow;
            }
            else
            {
                // 쿨다운 기반
                allowedToFire = (lastTriggerTime < 0f) || (Time.time - lastTriggerTime >= triggerCooldown);
            }

            if (durationMet && allowedToFire)
            {
                FireTrigger();
                firedInThisStableWindow = true;
            }
        }
        else
        {
            // 안정 구간 종료 → 다음 안정 진입 시 재발화 가능
            stableStartTime = -1f;
            stableElapsed = 0f;
            firedInThisStableWindow = false;
        }
    }

    void FireTrigger()
    {
        totalTriggers++;
        lastTriggerTime = Time.time;
        // stableStartTime은 유지 (oneShotPerStableWindow 모드에선 firedInThisStableWindow가 재발화 차단)
        // 사용자가 안정 깨야 다음 발화 가능

        Debug.Log($"[GyroTrigger] >>> TRIGGER #{totalTriggers} at t={Time.time:F2}s (gyro=({currentGyro.x:F3},{currentGyro.y:F3},{currentGyro.z:F3}))");
        OnTrigger?.Invoke();
    }

    public void ResetCounters()
    {
        totalTriggers = 0;
        lastTriggerTime = -1f;
        stableStartTime = -1f;
        stableElapsed = 0f;
        firedInThisStableWindow = false;
    }
}
