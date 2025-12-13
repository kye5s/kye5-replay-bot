const API_URL = "https://YOUR-RAILWAY-URL.up.railway.app/parse-replay";

async function uploadReplay() {
  const fileInput = document.getElementById("replayFile");
  const output = document.getElementById("output");

  if (!fileInput.files.length) {
    alert("Select a replay file first.");
    return;
  }

  const formData = new FormData();
  formData.append("file", fileInput.files[0]);

  output.textContent = "Processing replay...";

  try {
    const response = await fetch(API_URL, {
      method: "POST",
      body: formData
    });

    if (!response.ok) {
      const text = await response.text();
      throw new Error(text);
    }

    const data = await response.json();
    output.textContent = JSON.stringify(data, null, 2);
  } catch (err) {
    output.textContent = "Error:\n" + err.message;
  }
}
