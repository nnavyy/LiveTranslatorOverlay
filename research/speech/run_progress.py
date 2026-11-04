"""Progress timings and process/system memory snapshots; no model behavior changes."""
from datetime import datetime, timezone
import json
from pathlib import Path
import time

import psutil


class RunProgress:
    def __init__(self, output, started=None, device="cpu"):
        self.output = Path(output)
        self.process = psutil.Process()
        self.phase_started = time.monotonic() if started is None else started
        self.device = device

    def report(self, stage, done, total, active_seconds):
        memory = self.process.memory_info()
        system = psutil.virtual_memory()
        per_item = active_seconds / done if done else None
        remaining = per_item * (total - done) if per_item is not None else None
        record = dict(utc=datetime.now(timezone.utc).isoformat(), stage=stage, completed=done, total=total,
            stageActiveSeconds=active_seconds, phaseElapsedSeconds=time.monotonic()-self.phase_started,
            secondsPerItem=per_item, stageRemainingEstimateSeconds=remaining,
            processRssMiB=memory.rss/1024**2, processPrivateMiB=getattr(memory,"private",memory.vms)/1024**2,
            availableRamMiB=system.available/1024**2, totalRamMiB=system.total/1024**2,
            processPageFaults=getattr(memory,"num_page_faults",None))
        gpu_status = ""
        if self.device == "cuda":
            import torch
            record.update(gpuAllocatedMiB=torch.cuda.memory_allocated()/1024**2,
                          gpuReservedMiB=torch.cuda.memory_reserved()/1024**2,
                          gpuPeakAllocatedMiB=torch.cuda.max_memory_allocated()/1024**2)
            gpu_status = f" | GPU allocated/peak {record['gpuAllocatedMiB']:.0f}/{record['gpuPeakAllocatedMiB']:.0f} MiB"
        with (self.output/"progress.jsonl").open("a",encoding="utf-8") as stream:
            stream.write(json.dumps(record)+"\n")
        print(f"{stage}: {done}/{total} | {per_item:.2f} s/item | "
              f"stage ETA {remaining/60:.1f} min | phase elapsed {record['phaseElapsedSeconds']/60:.1f} min | "
              f"process RAM {record['processRssMiB']:.0f} MiB | available RAM {record['availableRamMiB']:.0f} MiB{gpu_status}",
              flush=True)
        if system.available < max(512*1024**2, system.total*.1):
            print("Low available RAM. Paging may slow this run; close memory-heavy applications if possible.", flush=True)
