import discord
from discord import app_commands
from discord.ext import commands
import os
import tempfile
import requests
from dotenv import load_dotenv

load_dotenv()
BOT_TOKEN = os.getenv("BOT_TOKEN")

intents = discord.Intents.default()
bot = commands.Bot(command_prefix='/', intents=intents)

# Platform → Emoji
PLATFORM_EMOJIS = {
    "PC": "(🖥️ PC)",
    "Xbox One": "(🎮 XBL)",
    "Xbox Series X/S": "(🎮 XBL)",
    "PlayStation": "(🎮 PS)",
    "Nintendo Switch": "(🎮 Switch)",
    "iOS": "(📱 iOS)",
    "Android": "(📱 Android)",
    "Mac": "(🍎 MAC)",
    "Unknown": "(🤖 AI)"
}

RARITY_EMOJI = {
    "Common": "⬜",
    "Uncommon": "🟩",
    "Rare": "🟦",
    "Epic": "🟪",
    "Legendary": "🟧",
    "Mythic": "🟨",
    "Unknown": "❔"
}


# ───────────────────────────────────────────────
# SLASH COMMAND: /replay
# ───────────────────────────────────────────────
@bot.tree.command(name="replay", description="Upload a Fortnite replay file to analyze kills.")
@app_commands.describe(file="Your .replay file")
async def replay(interaction: discord.Interaction, file: discord.Attachment):

    if not file.filename.endswith(".replay"):
        await interaction.response.send_message(
            "❌ Please upload a valid `.replay` file.",
            ephemeral=True
        )
        return

    await interaction.response.send_message("⏳ Processing your replay...", ephemeral=True)

    # Save the file temporarily
    temp_dir = tempfile.mkdtemp()
    local_path = os.path.join(temp_dir, file.filename)
    await file.save(local_path)

    url = "https://web-production-fe567.up.railway.app/parse-replay"

    with open(local_path, "rb") as f:
        response = requests.post(url, files={"replay_file": f})

    if response.status_code != 200:
        await interaction.followup.send(
            f"❌ Error reading replay: {response.json().get('error', 'Unknown error')}",
            ephemeral=True
        )
        return

    parsed = response.json()

    if "error" in parsed:
        await interaction.followup.send(
            f"❌ Parser error: {parsed['error']}",
            ephemeral=True
        )
        return

    furthest = parsed.get("furthest", {})
    final = parsed.get("final", {})

    # ───────────────────────────────────────────────
    # FORMAT A KILL BLOCK (each line separate)
    # ───────────────────────────────────────────────
    def fmt_kill(kill):
        rarity_icon = RARITY_EMOJI.get(kill.get("rarity", "Unknown"), "❔")
        weapon_display = f"{rarity_icon} {kill.get('weapon', 'Unknown')}"

        return (
            f"**Distance:**\n`{kill['distance']}m`\n"
            f"**Shooter:**\n{kill['killer']} {PLATFORM_EMOJIS.get(kill.get('killer_platform', 'Unknown'))}\n"
            f"**Hit:**\n{kill['victim']} {PLATFORM_EMOJIS.get(kill.get('victim_platform', 'Unknown'))}\n"
            f"**Weapon:**\n{weapon_display}"
        )

    # ───────────────────────────────────────────────
    # Create embed (SIDE-BY-SIDE layout preserved)
    # ───────────────────────────────────────────────
    embed = discord.Embed(
        title="🎬 kye5's Replay Checker",
        description="Replay Results:",
        color=0x5865F2
    )

    # Left column — Furthest Kill
    embed.add_field(
        name="🏆 Furthest Kill",
        value=fmt_kill(furthest),
        inline=True
    )

    # Spacer for layout balance
    embed.add_field(
        name="\u200b",
        value="\u200b",
        inline=True
    )

    # Right column — Final Kill
    embed.add_field(
        name="🎯 Final Kill",
        value=fmt_kill(final),
        inline=True
    )

    embed.set_footer(text="Powered by Trickshot Pro")

    await interaction.followup.send(embed=embed, ephemeral=False)


# ───────────────────────────────────────────────
# Sync command tree on startup
# ───────────────────────────────────────────────
@bot.event
async def on_ready():
    try:
        synced = await bot.tree.sync()
        print(f"Synced {len(synced)} commands.")
    except Exception as e:
        print("Sync error:", e)

    print(f"Bot online as {bot.user}")


bot.run(BOT_TOKEN)
