import json

# 자율 loop 전용: AskUserQuestion 호출을 가로채 차단하고, 모델에게 직접 결정하라고 피드백.
print(json.dumps({
    "hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": (
            "자율 loop 모드: AskUserQuestion 금지. 사소한 것 포함 묻지 말 것. "
            "가장 합리적인 기본값으로 직접 결정하고 loop-decisions.md에 '결정 / 근거' 1줄 기록 후 계속 진행. "
            "사람 행동 없이 못 푸는 진짜 block(인증·결제·외부 의존 실패)만 기록하고 다음으로 넘어간다."
        ),
    }
}))
