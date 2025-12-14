import os
import subprocess
import tempfile
import json

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

if not os.path.exists(PARSER_EXE):
    raise RuntimeError(f"Parser executable not found at {PARSER_EXE}")

os.chmod(PARSER_EXE, 0o755)

# ---- In-memory leaderboard ----
# Sorted by distance DESC
leaderboard = []


# ---- Health check ----
@app.get("/")
def root():
    return {"status": "ok"}


# ---- Leaderboard endpoint ----
@app.get("/leaderboard")
def get_leaderboard():
    return {
        "leaderboard": leaderboard[:10]  # top 10
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

        # ---- Update leaderboard ----
        if "furthest" in parsed:
            entry = {
                "distance": parsed["furthest"]["distance"],
                "player": parsed["furthest"]["killer"],
                "weapon": parsed["furthest"]["weapon"],
            }

            leaderboard.append(entry)
            leaderboard.sort(key=lambda x: x["distance"], reverse=True)

        return {
            "success": True,
            "output": result.stdout,
        }

    except subprocess.TimeoutExpired:
        raise HTTPException(status_code=500, detail="Parser timed out")

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
