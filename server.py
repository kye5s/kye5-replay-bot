import os
import subprocess
import tempfile
import json

from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from supabase import create_client, Client

# ---- App ----
app = FastAPI()

# ---- CORS ----
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ---- Supabase ----
SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")

if not SUPABASE_URL or not SUPABASE_ANON_KEY:
    raise RuntimeError("Missing Supabase environment variables")

supabase: Client = create_client(SUPABASE_URL, SUPABASE_ANON_KEY)

# ---- Paths ----
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARSER_EXE = os.path.join(BASE_DIR, "parser", "ParserApp")

if not os.path.exists(PARSER_EXE):
    raise RuntimeError(f"Parser executable not found at {PARSER_EXE}")

os.chmod(PARSER_EXE, 0o755)


# ---- Health check ----
@app.get("/")
def root():
    return {"status": "ok"}


# ---- Leaderboard endpoint ----
@app.get("/leaderboard")
def get_leaderboard():
    res = (
        supabase
        .table("leaderboard")
        .select("distance, player, weapon")
        .order("distance", desc=True)
        .execute()
    )

    return {"leaderboard": res.data}


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

        # ---- FINAL KILL → Leaderboard ----
        if "final" in parsed:
            final = parsed["final"]

            entry = {
                "distance": final["distance"],
                "player": final["killer"],
                "weapon": final["weapon"],
            }

            # ---- Duplicate check ----
            existing = (
                supabase
                .table("leaderboard")
                .select("id")
                .eq("distance", entry["distance"])
                .eq("player", entry["player"])
                .eq("weapon", entry["weapon"])
                .execute()
            )

            if not existing.data:
                supabase.table("leaderboard").insert(entry).execute()

        return {
            "success": True,
            "output": result.stdout,
        }

    except subprocess.TimeoutExpired:
        raise HTTPException(status_code=500, detail="Parser timed out")

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
