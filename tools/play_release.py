#!/usr/bin/env python3
"""Upload Builds/FirstPrinciples.aab to the Google Play *internal* track, then create the
one-time "Full Game" product (Play only allows that once a build declaring BILLING exists).

Usage (after building the signed AAB with BuildHelper.BuildRelease):
    PLAY_SA_JSON=~/Documents/keys/havit-play.json python3 tools/play_release.py

Never prints keys. The Plus subscriptions (firstprinciples_plus_monthly / _yearly) were
already created and activated through the API.
"""
import base64, json, os, subprocess, sys, tempfile, time, urllib.error, urllib.parse, urllib.request

PKG = "com.OrchAerospaceandGameGenesis.FirstPrinciples"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AAB = os.path.join(ROOT, "Builds", "FirstPrinciples.aab")
API = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/" + PKG
UPLOAD = "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/" + PKG


def token():
    k = json.load(open(os.path.expanduser(os.environ.get("PLAY_SA_JSON", "~/Documents/keys/havit-play.json"))))
    b64 = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=")
    now = int(time.time())
    h = b64(json.dumps({"alg": "RS256", "typ": "JWT"}).encode())
    c = b64(json.dumps({"iss": k["client_email"], "scope": "https://www.googleapis.com/auth/androidpublisher",
                        "aud": k["token_uri"], "iat": now, "exp": now + 3600}).encode())
    with tempfile.NamedTemporaryFile("w", delete=False) as f:
        f.write(k["private_key"])
        pk = f.name
    try:
        sig = subprocess.run(["openssl", "dgst", "-sha256", "-sign", pk], input=h + b"." + c, capture_output=True).stdout
    finally:
        os.remove(pk)
    body = urllib.parse.urlencode({"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
                                   "assertion": (h + b"." + c + b"." + b64(sig)).decode()}).encode()
    return json.load(urllib.request.urlopen(k["token_uri"], body))["access_token"]


TOK = token()


def call(url, method="GET", data=None, raw=None, ctype="application/json"):
    body = raw if raw is not None else (json.dumps(data).encode() if data is not None else None)
    req = urllib.request.Request(url, data=body, method=method,
                                 headers={"Authorization": "Bearer " + TOK, "Content-Type": ctype})
    try:
        r = urllib.request.urlopen(req, timeout=900).read()
        return json.loads(r) if r else {}
    except urllib.error.HTTPError as e:
        return {"err": e.code, "body": e.read().decode()[:800]}


def money(cur, s):
    u, n = s.split(".")
    return {"currencyCode": cur, "units": u, "nanos": int(n.ljust(9, "0"))}


def main():
    if not os.path.exists(AAB):
        sys.exit("Build the signed AAB first (BuildHelper.BuildRelease).")
    edit = call(API + "/edits", "POST", {})["id"]
    up = call(UPLOAD + f"/edits/{edit}/bundles?uploadType=media", "POST", raw=open(AAB, "rb").read(),
              ctype="application/octet-stream")
    if "versionCode" not in up:
        sys.exit(f"upload failed: {up}")
    vc = up["versionCode"]
    print("uploaded versionCode", vc)
    tr = call(API + f"/edits/{edit}/tracks/internal", "PUT", {
        "track": "internal",
        "releases": [{"name": "1.3", "versionCodes": [str(vc)], "status": "completed",
                      "releaseNotes": [{"language": "en-US", "text":
                          "Stars and mastery on every level, daily challenge with streaks, hints, achievements, "
                          "sound effects and a new level-complete screen. New: First Principles Plus."}]}]})
    print("track", tr.get("track") or tr)
    print("commit", call(API + f"/edits/{edit}:commit", "POST", {}).get("id", "failed"))

    conv = call(API + "/pricing:convertRegionPrices", "POST", {"price": money("USD", "4.99")})
    regs = [{"regionCode": r, "price": v["price"], "availability": "AVAILABLE"}
            for r, v in conv["convertedRegionPrices"].items()]
    body = {"packageName": PKG, "productId": "firstprinciples_full_unlock",
            "listings": [{"languageCode": "en-US", "title": "Full Game",
                          "description": "Unlock every level forever, including chapters added later."}],
            "purchaseOptions": [{"purchaseOptionId": "buy",
                                 "buyOption": {"legacyCompatible": True, "multiQuantityEnabled": False},
                                 "regionalPricingAndAvailabilityConfigs": regs,
                                 "newRegionsConfig": {"usdPrice": money("USD", "4.99"),
                                                      "eurPrice": conv["convertedOtherRegionsPrice"]["eurPrice"],
                                                      "availability": "AVAILABLE"}}]}
    rv = conv["regionVersion"]["version"]
    r = call(API + f"/onetimeproducts/firstprinciples_full_unlock?allowMissing=true&regionsVersion.version={rv}"
             "&updateMask=listings,purchaseOptions", "PATCH", body)
    print("full game product", r.get("productId") or r)
    r = call(API + "/oneTimeProducts/firstprinciples_full_unlock/purchaseOptions:batchUpdateStates", "POST",
             {"requests": [{"activatePurchaseOptionRequest": {"packageName": PKG,
                                                              "productId": "firstprinciples_full_unlock",
                                                              "purchaseOptionId": "buy"}}]})
    print("activate", "ok" if "err" not in r else r)


if __name__ == "__main__":
    main()
