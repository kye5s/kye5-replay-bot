import os
import subprocess
import tempfile

from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

# ---- App ----
app = FastAPI()

# ---- CORS (REQUIRED for GitHub Pages) ----
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # safe for this use case
    allow_methods=["*"],
    allow_headers=["*"],
)

# ---- Paths ----
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARSER_EXE = os.path.join(BASE_DIR, "parser", "ParserApp")

if not os.path.exists(PARSER_EXE):
    raise RuntimeError(f"Parser executable not found at {PARSER_EXE}")

# Ensure executable permissions (Render/Linux)
os.chmod(PARSER_EXE, 0o755)


# ---- Health check ----
@app.get("/")
def root():
    return {"status": "ok"}


# ---- Replay parsing endpoint ----
@app.post("/parse-replay")
async def parse_replay(file: UploadFile = File(...)):
    if not file.filename.endswith(".replay"):
        raise HTTPException(status_code=400, detail="Invalid file type")

    try:
        # Save upload to temp file
        with tempfile.NamedTemporaryFile(delete=False, suffix=".replay") as tmp:
            temp_path = tmp.name
            contents = await file.read()
            tmp.write(contents)

        # Run parser
        result = subprocess.run(
            [PARSER_EXE, temp_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=60,
        )

        # Clean up temp file
        os.remove(temp_path)

        if result.returncode != 0:
            return JSONResponse(
                status_code=500,
                content={
                    "error": "Parser failed",
                    "stderr": result.stderr,
                },
            )

        return {
            "success": True,
            "output": result.stdout,
        }

    except subprocess.TimeoutExpired:
        raise HTTPException(status_code=500, detail="Parser timed out")

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
