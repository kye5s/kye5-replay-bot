import uvicorn
import subprocess
import uuid
import os
from fastapi import FastAPI, UploadFile, File
from fastapi.responses import JSONResponse

app = FastAPI()

# Ensure parser folder exists
PARSER_PATH = os.path.join(os.getcwd(), "parser", "ParserApp.exe")

@app.post("/parse-replay")
async def parse_replay(file: UploadFile = File(...)):
    try:
        # Save temporarily
        temp_name = f"{uuid.uuid4()}.replay"
        temp_path = os.path.join("/tmp", temp_name)

        with open(temp_path, "wb") as f:
            f.write(await file.read())

        # Run C# parser
        process = subprocess.Popen(
            [PARSER_PATH, temp_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )

        stdout, stderr = process.communicate()

        if stderr:
            return JSONResponse({"error": stderr}, status_code=500)

        # Clean output if necessary
        stdout = stdout.strip()

        return JSONResponse({"result": stdout})

    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=500)
