import os
import subprocess
import tempfile
import json
import threading

from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

# ---- App ----
app = FastAPI()

# ---- CORS ----
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ---- Paths ----
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARSER_EXE = os.path.join(BASE_DIR, "parser", "ParserApp")
LEADERBOARD_FILE = os.path.join(BASE_DIR, "leaderboard.json")

if not os.path.exists(PARSER_EXE):
    raise RuntimeError(f"Parser executable not found at {PARSER_EXE}")

os.chmod(PARSER_EXE, 0o755)

# ---- Thread lock (prevents race conditions) ----
leaderboard_lock = threading.Lock()

# ---- Load leaderboard on startup ----
def load_leaderboard():
    if not os.path.exists(LEADERBOARD_FILE):
        return []

    try:
        with open(LEADERBOARD_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return []

def save_leaderboard(data):
    with open(LEADERBOARD_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)

leaderboard = load_leaderboard()

# ---- Health check ----
@app.get("/")
def root():
    return {"status": "ok"}

# ---- Leaderboard endpoint ----
@app.get("/leaderboard")
def get_leaderboard():
    return {
        "leaderboard": leaderboard[:10]
    }

# ---- Replay parsing endpoint ----
@app.post("/parse-replay")
async def parse_replay(file: UploadFile = File(...)):
    if not file.filename.endswith(".replay"):
        raise HTTPException(status_code=400, detail="Invalid file type")

    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix=".replay") as tmp:
            temp_path = tmp.name
            tmp.write(await file.read())

        result = subprocess.run(
            [PARSER_EXE, temp_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=60,
        )

        os.remove(temp_path)

        if result.returncode != 0:
            return JSONResponse(
                status_code=500,
                content={"error": "Parser failed", "stderr": result.stderr},
            )

        parsed = json.loads(result.stdout)

        # ---- Update leaderboard using FINAL KILL ----
        if "final" in parsed:
            final = parsed["final"]

            entry = {
                "distance": final["distance"],
                "player": final["killer"],
                "weapon": final["weapon"],
            }

            with leaderboard_lock:
                # Prevent duplicates (same distance + player + weapon)
                exists = any(
                    e["distance"] == entry["distance"]
                    and e["player"] == entry["player"]
                    and e["weapon"] == entry["weapon"]
                    for e in leaderboard
                )

                if not exists:
                    leaderboard.append(entry)
                    leaderboard.sort(key=lambda x: x["distance"], reverse=True)
                    save_leaderboard(leaderboard)

        return {
            "success": True,
            "output": result.stdout,
        }

    except subprocess.TimeoutExpired:
        raise HTTPException(status_code=500, detail="Parser timed out")

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
