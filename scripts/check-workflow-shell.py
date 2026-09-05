#!/usr/bin/env python3
"""Syntax-check literal sh/bash run blocks without executing workflow commands."""

import re
import subprocess
import sys
from pathlib import Path


def check(path):
    lines = path.read_text().splitlines()
    shell = "bash"
    checked = 0
    for index, line in enumerate(lines):
        if re.match(r"\s+- (name:|uses:|run:)", line):
            shell = "bash"
        match = re.match(r"\s+shell:\s*(\S+)", line)
        if match:
            shell = match[1]
        match = re.match(r"(\s+)(?:- )?run:\s*(.*)", line)
        if not match or shell not in ("bash", "sh"):
            continue
        body = match[2]
        if body.startswith(("|", ">")):
            indent = len(match[1])
            block = []
            for following in lines[index + 1:]:
                if following.strip() and len(following) - len(following.lstrip()) <= indent:
                    break
                block.append(following)
            body = "\n".join(block)
        # Expressions are expanded by GitHub before a shell sees the script.
        body = re.sub(r"\$\{\{.*?\}\}", "workflow_value", body)
        result = subprocess.run([shell, "-n"], input=body, text=True, capture_output=True)
        if result.returncode:
            sys.exit(f"{path}:{index + 1}: {result.stderr.strip()}")
        checked += 1
    print(f"{path}: checked {checked} shell blocks")


for argument in sys.argv[1:]:
    check(Path(argument))
