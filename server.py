from fastapi import FastAPI, UploadFile, File
import subprocess
import uuid
import os
import json

app = FastAPI()

PARSER_EXE = "/app/parser/ParserApp.exe"   # <-- your actual path inside Railway

@app.post("/parse-replay")
async def parse_replay(replay_file: UploadFile = File(...)):
    # Save uploaded replay
    temp_name = f"/tmp/{uuid.uuid4()}.replay"
    with open(temp_name, "wb") as f:
        f.write(await replay_file.read())

    # Call the parser EXE
    result = subprocess.run(
        [PARSER_EXE, temp_name],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    if result.returncode != 0:
        return {"error": result.stderr}

    # Output should be JSON from ParserApp.exe
    try:
        data = json.loads(result.stdout)
        return data
    except:
        return {
            "error": "Parser returned invalid output",
            "raw": result.stdout,
            "stderr": result.stderr
        }
