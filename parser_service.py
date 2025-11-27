from flask import Flask, request, jsonify
import subprocess
import tempfile
import os
import json

app = Flask(__name__)

# Set the path to ParserApp.exe
REPLAY_PARSER_PATH = r'C:\Users\eleme\Desktop\fortnite-discord-bot\parser_dist\ParserApp.exe'

@app.route('/parse-replay', methods=['POST'])
def parse_replay():
    if 'replay_file' not in request.files:
        return jsonify({"error": "No file part"}), 400

    replay_file = request.files['replay_file']
    if replay_file.filename == '':
        return jsonify({"error": "No selected file"}), 400

    # Save the file temporarily
    temp_dir = tempfile.mkdtemp()
    temp_file_path = os.path.join(temp_dir, replay_file.filename)
    replay_file.save(temp_file_path)

    # Debugging: print the file path being used
    print(f"Parsing replay file: {temp_file_path}")

    try:
        # Run the ParserApp.exe to parse the replay file
        result = subprocess.run(
            [REPLAY_PARSER_PATH, temp_file_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )

        # Debugging: print stdout and stderr for troubleshooting
        print(f"ParserApp stdout: {result.stdout}")
        print(f"ParserApp stderr: {result.stderr}")

        if result.returncode != 0:
            return jsonify({"error": result.stderr}), 500

        # Assuming the output is JSON, parse it
        output = result.stdout
        print(f"Parser output: {output}")  # Added debug print for output
        try:
            # Extract JSON block safely
            json_start = output.find("{")
            json_end = output.rfind("}")

            if json_start == -1 or json_end == -1:
                return jsonify({"error": "Parser output was not valid JSON"}), 500

            json_text = output[json_start:json_end + 1]

            parsed_output = json.loads(json_text)

        except ValueError:
            return jsonify({"error": "Failed to parse the replay data."}), 500

        return jsonify(parsed_output)

    except Exception as e:
        return jsonify({"error": str(e)}), 500
    finally:
        # Clean up the temporary file
        try:
            os.remove(temp_file_path)
        except:
            pass

if __name__ == '__main__':
    app.run(debug=True, host='0.0.0.0', port=5000)
