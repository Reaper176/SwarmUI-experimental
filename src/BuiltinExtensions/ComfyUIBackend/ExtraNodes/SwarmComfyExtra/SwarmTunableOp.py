import os
import threading
import time

NODE_CLASS_MAPPINGS = {}

def _run_tunableop_flush_loop():
    try:
        import torch
        import torch.cuda.tunable as tunable
    except Exception as ex:
        print(f"[Swarm] TunableOp flush helper unavailable: {ex}")
        return
    if not tunable.is_enabled():
        print("[Swarm] TunableOp flush helper disabled: TunableOp is not enabled.")
        return
    interval = 15.0
    try:
        interval = float(os.environ.get("SWARM_TUNABLEOP_FLUSH_SECONDS", "15"))
    except Exception:
        pass
    if interval <= 0:
        print("[Swarm] TunableOp flush helper disabled by SWARM_TUNABLEOP_FLUSH_SECONDS.")
        return
    print(
        f"[Swarm] TunableOp flush helper active: "
        f"filename='{os.environ.get('PYTORCH_TUNABLEOP_FILENAME', '')}', "
        f"tuning={tunable.tuning_is_enabled()}, "
        f"record_untuned={tunable.record_untuned_is_enabled()}, "
        f"interval={interval}s"
    )
    while True:
        time.sleep(interval)
        try:
            count = len(tunable.get_results())
            wrote = tunable.write_file()
            if count > 0 or wrote:
                print(f"[Swarm] TunableOp flush: results={count}, wrote={wrote}, file='{tunable.get_filename()}'")
        except Exception as ex:
            print(f"[Swarm] TunableOp flush failed: {ex}")

threading.Thread(target=_run_tunableop_flush_loop, daemon=True).start()
