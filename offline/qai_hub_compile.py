"""
Qualcomm AI Hub — ONNX → QNN 컴파일 + 레이턴시 프로파일

타겟: XR2 Gen 2 (Proxy), chipset qcs8450, Hexagon v69
    → AR1 Gen 1의 closest proxy (둘 다 XR class + Hexagon v69)

전략:
    1. FP16 컴파일 먼저 (calibration data 불필요, 빠른 검증)
    2. NPU(HTP)에 배치되는지 확인
    3. 레이턴시가 200ms 안에 들어오는지 확인
    4. (선택) INT8 calibration 으로 추가 최적화는 다음 라운드

사용법:
    python qai_hub_compile.py yolo
    python qai_hub_compile.py clip
    python qai_hub_compile.py both
"""

import sys
import time
from pathlib import Path

import qai_hub as hub

DEVICE_NAME = "XR2 Gen 2 (Proxy)"

JOBS = {
    "yolo": {
        "name": "Eagle Eye YOLO11n",
        "model": "yolo11n.onnx",
        "input_specs": {"images": (1, 3, 320, 320)},
    },
    "clip": {
        "name": "Eagle Eye MobileCLIP-S2",
        "model": "mobileclip_s2_image.onnx",
        "input_specs": {"image": (1, 3, 256, 256)},
    },
}


def submit_compile(key):
    cfg = JOBS[key]
    print(f"\n[{key}] Submitting compile job ...")
    print(f"    model:  {cfg['model']}")
    print(f"    device: {DEVICE_NAME}")
    print(f"    input:  {cfg['input_specs']}")

    job = hub.submit_compile_job(
        model=cfg["model"],
        device=hub.Device(DEVICE_NAME),
        input_specs=cfg["input_specs"],
        name=f"{cfg['name']} - Compile",
        options="--target_runtime qnn_context_binary --compute_unit npu",
    )
    print(f"    Compile job ID: {job.job_id}")
    print(f"    Dashboard URL:  {job.url}")
    return job


def wait_compile(job):
    print(f"\nWaiting for compile job {job.job_id} ...")
    job.wait()
    if not job.get_status().success:
        print(f"FAILED: {job.get_status().message}")
        return None
    target_model = job.get_target_model()
    print(f"Compile OK. Target model: {target_model}")
    return target_model


def submit_profile(key, target_model):
    cfg = JOBS[key]
    print(f"\n[{key}] Submitting profile job (latency measurement) ...")
    job = hub.submit_profile_job(
        model=target_model,
        device=hub.Device(DEVICE_NAME),
        name=f"{cfg['name']} - Profile",
    )
    print(f"    Profile job ID: {job.job_id}")
    print(f"    Dashboard URL:  {job.url}")
    return job


def wait_profile(job, key):
    print(f"\nWaiting for profile job {job.job_id} ...")
    job.wait()
    if not job.get_status().success:
        print(f"FAILED: {job.get_status().message}")
        return None

    profile = job.download_profile()
    summary = profile.get("execution_summary", {})
    print(f"\n=== {key} Profile Result ===")
    print(f"  inference_time (us): {summary.get('estimated_inference_time')}")
    print(f"  inference_memory_peak_bytes: {summary.get('inference_memory_peak_range')}")
    print(f"  compute_unit_breakdown: {summary.get('compute_unit_execution_durations', {})}")

    layer_info = profile.get("execution_detail", [])
    npu_count = sum(1 for l in layer_info if l.get("compute_unit") == "NPU")
    cpu_count = sum(1 for l in layer_info if l.get("compute_unit") == "CPU")
    gpu_count = sum(1 for l in layer_info if l.get("compute_unit") == "GPU")
    print(f"  layer placement: NPU={npu_count}  CPU={cpu_count}  GPU={gpu_count}")
    return profile


def download_target(key, target_model):
    out = f"{Path(JOBS[key]['model']).stem}.qnn_context.bin"
    target_model.download(out)
    print(f"\nDownloaded compiled binary: {out}")
    return out


def run(target):
    keys = ["yolo", "clip"] if target == "both" else [target]

    compile_jobs = {k: submit_compile(k) for k in keys}

    target_models = {}
    for k, j in compile_jobs.items():
        m = wait_compile(j)
        if m is None:
            print(f"Skipping {k} due to compile failure")
            continue
        target_models[k] = m

    profile_jobs = {k: submit_profile(k, m) for k, m in target_models.items()}
    profiles = {}
    for k, j in profile_jobs.items():
        p = wait_profile(j, k)
        if p:
            profiles[k] = p

    for k, m in target_models.items():
        download_target(k, m)

    return profiles


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "both"
    if target not in ("yolo", "clip", "both"):
        print(f"Usage: python qai_hub_compile.py [yolo|clip|both]")
        sys.exit(1)
    run(target)
