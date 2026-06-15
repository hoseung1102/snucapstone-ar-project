using System;
using System.Collections.Generic;
using UnityEngine;

// AmbientInterestProfile (AIP) — v0.5.1
//
// 노션 spec 3.3:
//   "passing-by 데이터를 실시간 광고 트리거가 아닌, 온디바이스 누적 프로필로 활용"
//   v1 에서는 데이터 스키마 정의만, 실제 활용은 v2+
//
// 핵심 원칙:
//   - 100% 온디바이스 (원본 frame 외부 전송 금지, 노션 10.1)
//   - 카테고리 / class_id / 잠재의식적 attention 시간 누적
//   - 추후 dwell 모먼트에서 입찰 단가 boost 용 데이터
//
// 데이터 형식:
//   - Event: 한 트리거에서 잡힌 객체의 passing-by 기록
//   - Profile: 누적된 카테고리/SKU 별 attention 합계
//   - 저장: persistentDataPath/aip.json

[Serializable]
public class AIPEvent
{
    public long timestamp_ms;      // Unix epoch milliseconds
    public int class_id;           // COCO class ID (0~79)
    public string class_name;      // e.g. "bottle", "laptop"
    public float confidence;       // YOLO conf
    public float duration_sec;     // 객체가 시야 안에 머문 시간 (proxy: 단일 frame 만 잡혔으면 0)
    public string product_name;    // CLIP 매칭 결과 (null 가능)
    public float product_sim;      // CLIP similarity (매칭 시)
}

[Serializable]
public class AIPProfile
{
    public List<AIPEvent> events = new List<AIPEvent>();
    public long created_ms;
    public long last_updated_ms;
    public int version = 1;   // 스키마 버전
}

public class AmbientInterestProfile : MonoBehaviour
{
    [Header("저장")]
    [Tooltip("파일명. persistentDataPath/{aip.json} 에 저장")]
    public string filename = "aip.json";

    [Tooltip("저장 빈도 — N event 마다 disk write")]
    public int saveEveryNEvents = 5;

    [Tooltip("최대 보관 event 수 (memory). 초과 시 oldest 부터 drop")]
    public int maxEvents = 1000;

    AIPProfile _profile;
    int _unsavedCount = 0;

    void Awake()
    {
        Load();
    }

    public void Log(int classId, string className, float confidence,
                    string productName = null, float productSim = -1f, float durationSec = 0f)
    {
        if (_profile == null) _profile = new AIPProfile { created_ms = NowMs() };
        var ev = new AIPEvent {
            timestamp_ms = NowMs(),
            class_id = classId,
            class_name = className,
            confidence = confidence,
            duration_sec = durationSec,
            product_name = productName,
            product_sim = productSim,
        };
        _profile.events.Add(ev);
        _profile.last_updated_ms = ev.timestamp_ms;

        // memory bound
        if (_profile.events.Count > maxEvents)
            _profile.events.RemoveRange(0, _profile.events.Count - maxEvents);

        _unsavedCount++;
        if (_unsavedCount >= saveEveryNEvents) Save();
    }

    public void Save()
    {
        try {
            string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(_profile));
            _unsavedCount = 0;
            Debug.Log($"[AIP] saved {path} ({_profile.events.Count} events)");
        } catch (Exception e) {
            Debug.LogWarning($"[AIP] save 실패: {e.Message}");
        }
    }

    void Load()
    {
        try {
            string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            if (System.IO.File.Exists(path)) {
                _profile = JsonUtility.FromJson<AIPProfile>(System.IO.File.ReadAllText(path));
                Debug.Log($"[AIP] loaded {path} ({_profile.events.Count} events)");
            } else {
                _profile = new AIPProfile { created_ms = NowMs() };
            }
        } catch (Exception e) {
            Debug.LogWarning($"[AIP] load 실패: {e.Message}");
            _profile = new AIPProfile { created_ms = NowMs() };
        }
    }

    void OnApplicationQuit() { Save(); }
    void OnApplicationPause(bool p) { if (p) Save(); }

    static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // v2+ stub — 카테고리/SKU 별 attention 시간 합계 등
    public int GetEventCount() => _profile?.events?.Count ?? 0;
}
