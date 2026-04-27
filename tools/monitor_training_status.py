import argparse
import csv
import json
import os
import statistics
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Monitor OverTheStrait training progress without Unity graphics."
    )
    parser.add_argument("--run-id", default="hormuz_run1", help="ML-Agents run id")
    parser.add_argument("--interval", type=float, default=2.0, help="Refresh interval in seconds")
    parser.add_argument("--once", action="store_true", help="Print once and exit")
    parser.add_argument("--logs-dir", default="logs", help="Logs directory relative to repo root")
    parser.add_argument("--results-dir", default="results", help="Results directory relative to repo root")
    return parser.parse_args()


def repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def read_csv_rows(path: Path) -> List[Dict[str, Any]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def read_json(path: Path):
    if not path.exists():
        return None
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def safe_int(value, default=0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def safe_float(value, default=0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def mean_or_zero(values: List[float]) -> float:
    return statistics.mean(values) if values else 0.0


def summarize_recent(rows: List[Dict[str, Any]], window: int) -> Dict[str, float]:
    recent = rows[-window:]
    if not recent:
        return {
            "count": 0,
            "goal_rate": 0.0,
            "avg_reward": 0.0,
            "best_reward": 0.0,
        }

    totals = [safe_int(r.get("Total"), 0) for r in recent]
    goals = [safe_int(r.get("GoalReached"), 0) for r in recent]
    avg_rewards = [safe_float(r.get("AvgReward"), 0.0) for r in recent]
    best_rewards = [safe_float(r.get("BestReward"), 0.0) for r in recent]

    total_episodes = sum(totals) or 1
    return {
        "count": len(recent),
        "goal_rate": sum(goals) / total_episodes,
        "avg_reward": mean_or_zero(avg_rewards),
        "best_reward": mean_or_zero(best_rewards),
    }


def latest_file(paths: List[Path]) -> Optional[Path]:
    existing = [p for p in paths if p.exists()]
    if not existing:
        return None
    return max(existing, key=lambda p: p.stat().st_mtime)


def format_age(ts: Optional[float]) -> str:
    if ts is None:
        return "-"
    seconds = max(0, time.time() - ts)
    if seconds < 60:
        return f"{int(seconds)}s ago"
    if seconds < 3600:
        return f"{int(seconds // 60)}m {int(seconds % 60)}s ago"
    return f"{int(seconds // 3600)}h {int((seconds % 3600) // 60)}m ago"


def format_timestamp(ts: Optional[float]) -> str:
    if ts is None:
        return "-"
    return datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M:%S")


def find_artifacts(results_dir: Path, run_id: str) -> dict:
    run_dir = results_dir / run_id
    checkpoint = latest_file(list(run_dir.rglob("checkpoint.pt")))
    onnx = latest_file(list(run_dir.rglob("*.onnx")))
    training_status = read_json(run_dir / "run_logs" / "training_status.json")

    steps = None
    if training_status:
        behavior = next(
            (v for k, v in training_status.items() if k != "metadata" and isinstance(v, dict)),
            None,
        )
        if behavior:
            final_checkpoint = behavior.get("final_checkpoint") or {}
            steps = final_checkpoint.get("steps")

    return {
        "checkpoint": checkpoint,
        "onnx": onnx,
        "steps": steps,
    }


def build_assessment(status: Optional[Dict[str, Any]], rows: List[Dict[str, Any]]) -> str:
    recent20 = summarize_recent(rows, 20)
    recent10 = summarize_recent(rows, 10)

    if recent20["goal_rate"] > 0:
        return "Goal reached episodes are appearing. Start tracking whether success becomes stable."

    if status:
        collisions = safe_int(status.get("collisionThisGeneration"), 0)
        timeouts = safe_int(status.get("timeoutThisGeneration"), 0)
        if collisions > timeouts * 1.5 and collisions >= 5:
            return "Collisions dominate. The agent still struggles with direction control or terrain avoidance."
        if timeouts > collisions * 1.5 and timeouts >= 5:
            return "Timeouts dominate. The agent survives longer, but still is not committing to the goal strongly enough."

    if recent10["avg_reward"] >= -0.35:
        return "Recent rewards suggest many timeout endings rather than immediate crashes. Navigation is improving, but goal completion is still missing."
    if recent10["avg_reward"] <= -0.9:
        return "Recent rewards are crash-heavy. Focus on easier starts, clearer heading bias, or shorter initial routes."
    return "Progress is mixed. Watch whether recent average reward trends upward and whether the first goal appears."


def clear_screen():
    os.system("cls" if os.name == "nt" else "clear")


def print_report(
    status: Optional[Dict[str, Any]],
    rows: List[Dict[str, Any]],
    artifacts: Dict[str, Any],
    run_id: str,
):
    recent10 = summarize_recent(rows, 10)
    recent50 = summarize_recent(rows, 50)
    last_row = rows[-1] if rows else None

    print("=" * 78)
    print(f"OverTheStrait Training Monitor  |  Run ID: {run_id}")
    print("=" * 78)

    if status:
        print(
            "Current Gen: "
            f"{safe_int(status.get('currentGeneration')):04d}  |  "
            f"Progress: {safe_int(status.get('episodesCompletedThisGeneration'))}/"
            f"{safe_int(status.get('totalEpisodesThisGeneration'))}  |  "
            f"G/C/T: {safe_int(status.get('goalReachedThisGeneration'))}/"
            f"{safe_int(status.get('collisionThisGeneration'))}/"
            f"{safe_int(status.get('timeoutThisGeneration'))}"
        )
        print(
            "Elapsed: "
            f"{safe_float(status.get('generationElapsedRealSeconds')):.1f}s  |  "
            f"Episode Limit: {safe_float(status.get('maxEpisodeRealSeconds')):.1f}s real  |  "
            f"TimeScale: {safe_float(status.get('timeScale')):.1f}x  |  "
            f"Updated: {status.get('lastUpdateLocal', '-')}"
        )
    else:
        print("Current Gen: waiting for training_runtime_status.json")

    if last_row:
        print(
            "Last Completed Gen: "
            f"{safe_int(last_row.get('Generation')):04d}  |  "
            f"Best {safe_float(last_row.get('BestReward')):.3f}  |  "
            f"Avg {safe_float(last_row.get('AvgReward')):.3f}  |  "
            f"Goal {safe_int(last_row.get('GoalReached'))}/{safe_int(last_row.get('Total'))}  |  "
            f"{last_row.get('Timestamp', '-')}"
        )
    else:
        print("Last Completed Gen: no training_history.csv rows yet")

    print("-" * 78)
    print(
        f"Recent 10 Gens : goal rate {recent10['goal_rate'] * 100:5.1f}%  |  "
        f"avg reward {recent10['avg_reward']:7.3f}  |  best reward {recent10['best_reward']:7.3f}"
    )
    print(
        f"Recent 50 Gens : goal rate {recent50['goal_rate'] * 100:5.1f}%  |  "
        f"avg reward {recent50['avg_reward']:7.3f}  |  best reward {recent50['best_reward']:7.3f}"
    )

    run_best = status.get("runBestEpisode") if status else None
    if run_best:
        print(
            "Run Best Episode: "
            f"Gen {safe_int(run_best.get('generation')):04d}  |  "
            f"Score {safe_float(run_best.get('score')):.3f}  |  "
            f"{run_best.get('endReason', '-')}  |  {run_best.get('agentName', '-')}"
        )
    else:
        print("Run Best Episode: waiting for first finished episode")

    last_completed = status.get("lastCompletedGeneration") if status else None
    if last_completed:
        print(
            "Last Gen Detail : "
            f"G/C/T = {safe_int(last_completed.get('goalReached'))}/"
            f"{safe_int(last_completed.get('collisionCount'))}/"
            f"{safe_int(last_completed.get('timeoutCount'))}  |  "
            f"Best Agent {last_completed.get('bestAgent', '-')}"
        )

    print("-" * 78)
    checkpoint = artifacts["checkpoint"]
    onnx = artifacts["onnx"]
    print(
        "Checkpoint     : "
        f"{checkpoint if checkpoint else '-'}"
    )
    if checkpoint:
        stat = checkpoint.stat()
        print(
            f"  updated {format_timestamp(stat.st_mtime)}  |  {format_age(stat.st_mtime)}  |  {stat.st_size} bytes"
        )

    print("ONNX           : " f"{onnx if onnx else '-'}")
    if onnx:
        stat = onnx.stat()
        print(
            f"  updated {format_timestamp(stat.st_mtime)}  |  {format_age(stat.st_mtime)}  |  {stat.st_size} bytes"
        )

    steps = artifacts.get("steps")
    print(f"ML-Agents Step : {steps if steps is not None else '-'}")
    print("-" * 78)
    print("Assessment     : " + build_assessment(status, rows))


def main():
    args = parse_args()
    root = repo_root()
    logs_dir = root / args.logs_dir
    results_dir = root / args.results_dir
    csv_path = logs_dir / "training_history.csv"
    status_path = logs_dir / "training_runtime_status.json"

    while True:
        rows = read_csv_rows(csv_path)
        status = read_json(status_path) or {}
        artifacts = find_artifacts(results_dir, args.run_id)

        if not args.once:
            clear_screen()
        print_report(status, rows, artifacts, args.run_id)

        if args.once:
            return
        time.sleep(max(0.5, args.interval))


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
