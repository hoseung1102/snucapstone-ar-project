"""
Step B 백그라운드 러너 — 컴파일 + 프로파일 + 다운로드 + 결과 기록

이미 제출된 YOLO 컴파일 잡 (jgnxo3kq5)을 받아들이고,
MobileCLIP 컴파일 잡을 새로 제출한 뒤 둘 다 완료까지 대기.
이후 두 모델 모두 profile 잡 제출, 완료 후 .bin 다운로드.

모든 진행 상황은 stdout + qai_hub_pipeline.log 에 기록.
"""

import sys
import time
import json
from datetime import datetime
from pathlib import Path

import qai_hub as hub

DEVICE_NAME = "XR2 Gen 2 (Proxy)"
YOLO_COMPILE_JOB_ID = "jp0kdl6n5"
CLIP_COMPILE_JOB_ID = "jp886z1op"

LOG = open("qai_hub_pipeline.log", "a", buffering=1)


def log(msg):
    line = f"[{datetime.now().isoformat(timespec='seconds')}] {msg}"
    print(line)
    LOG.write(line + "\n")


def submit_clip_compile():
    log("Submitting MobileCLIP-S2 compile job...")
    job = hub.submit_compile_job(
        model="mobileclip_s2_image.onnx",
        device=hub.Device(DEVICE_NAME),
        input_specs={"image": (1, 3, 256, 256)},
        name="Eagle Eye MobileCLIP-S2 - Compile",
        options="--target_runtime qnn_context_binary --compute_unit npu",
    )
    log(f"  CLIP compile job: {job.job_id}  ({job.url})")
    return job


def wait_for(job, label):
    log(f"Waiting for {label} job {job.job_id}...")
    last_state = None
    while True:
        status = job.get_status()
        if status.state.name != last_state:
            log(f"  {label} state: {status.state.name}")
            last_state = status.state.name
        if status.finished:
            break
        time.sleep(20)
    if not status.success:
        log(f"  {label} FAILED: {status.message}")
        return False
    log(f"  {label} SUCCESS")
    return True


def profile_and_download(model_key, target_model, onnx_filename):
    log(f"Submitting profile job for {model_key}...")
    pjob = hub.submit_profile_job(
        model=target_model,
        device=hub.Device(DEVICE_NAME),
        name=f"Eagle Eye {model_key} - Profile",
    )
    log(f"  profile job: {pjob.job_id}  ({pjob.url})")
    if not wait_for(pjob, f"{model_key} profile"):
        return None

    profile = pjob.download_profile()

    summary = {}
    if isinstance(profile, dict):
        es = profile.get("execution_summary", {})
        summary = {
            "estimated_inference_time_us": es.get("estimated_inference_time"),
            "compute_unit_breakdown": es.get("compute_unit_execution_durations", {}),
        }

    out_bin = f"{Path(onnx_filename).stem}.qnn_context.bin"
    target_model.download(out_bin)
    log(f"  downloaded: {out_bin}")

    return {"profile_job_id": pjob.job_id, "summary": summary, "binary": out_bin}


def main():
    log("=" * 60)
    log("Eagle Eye QNN Compilation Pipeline")
    log(f"Device: {DEVICE_NAME}")
    log("=" * 60)

    yolo_compile = hub.get_job(YOLO_COMPILE_JOB_ID)
    log(f"Recovered YOLO compile job: {yolo_compile.job_id}")

    clip_compile = hub.get_job(CLIP_COMPILE_JOB_ID)
    log(f"Recovered CLIP compile job: {clip_compile.job_id}")

    yolo_ok = wait_for(yolo_compile, "YOLO compile")
    clip_ok = wait_for(clip_compile, "CLIP compile")

    results = {}

    if yolo_ok:
        yolo_model = yolo_compile.get_target_model()
        results["yolo"] = profile_and_download("yolo", yolo_model, "yolo11n.onnx")
    else:
        results["yolo"] = {"error": "compile failed"}

    if clip_ok:
        clip_model = clip_compile.get_target_model()
        results["clip"] = profile_and_download("clip", clip_model, "mobileclip_s2_image.onnx")
    else:
        results["clip"] = {"error": "compile failed"}

    log("=" * 60)
    log("FINAL RESULTS")
    log("=" * 60)
    log(json.dumps(results, indent=2, default=str))

    Path("qai_hub_results.json").write_text(json.dumps(results, indent=2, default=str))
    log("Saved: qai_hub_results.json")


if __name__ == "__main__":
    main()
