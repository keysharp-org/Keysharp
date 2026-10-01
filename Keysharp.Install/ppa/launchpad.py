#!/usr/bin/env python3
"""Anonymous Launchpad queries for the PPA scripts.

launchpad.py has-version OWNER/PPA SOURCE VERSION
    Exits 0 when the PPA has ever published SOURCE at VERSION. Launchpad accepts a
    version once, so an upload of it again can only be rejected.
launchpad.py orig-url OWNER/PPA SOURCE UPSTREAM_VERSION
    Prints the URL of the upstream tarball the PPA already holds for UPSTREAM_VERSION.
    Every later upload of that version must carry the identical file.
"""

import json
import sys
import urllib.parse
import urllib.request

API = "https://api.launchpad.net/1.0"


def get(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def sources(ppa, source):
    owner, name = ppa.split("/", 1)
    query = urllib.parse.urlencode(
        {"ws.op": "getPublishedSources", "source_name": source, "exact_match": "true"})
    url = f"{API}/~{owner}/+archive/ubuntu/{name}?{query}"
    while url:
        page = get(url)
        yield from page["entries"]
        url = page.get("next_collection_link")


def main(command, ppa, source, version):
    if command == "has-version":
        return 0 if any(s["source_package_version"] == version for s in sources(ppa, source)) else 1
    if command == "orig-url":
        for publication in sources(ppa, source):
            if publication["source_package_version"].startswith(version + "-"):
                for url in get(publication["self_link"] + "?ws.op=sourceFileUrls"):
                    if f"_{version}.orig.tar." in url:
                        print(url)
                        return 0
        return 1
    sys.exit(__doc__)


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    sys.exit(main(*sys.argv[1:]))
