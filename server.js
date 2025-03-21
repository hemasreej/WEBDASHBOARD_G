const express = require("express");
const cors = require("cors");
const WebSocket = require("ws");

const app = express();
app.use(cors()); // Allow cross-origin requests
app.use(express.json()); // Parse JSON body

const PORT = 5000;

// Create WebSocket server
const wss = new WebSocket.Server({ noServer: true });
let clients = [];

// WebSocket connection handling
wss.on("connection", (ws) => {
    console.log("🔌 Client connected via WebSocket");
    clients.push(ws);

    ws.on("close", () => {
        clients = clients.filter(client => client !== ws);
        console.log("❌ Client disconnected");
    });

    ws.on("error", (error) => {
        console.error("⚠ WebSocket Error:", error.message);
    });
});

// Start height measurement
app.get("/startHeight", (req, res) => {
    console.log("📏 Height measurement triggered!");

    // Notify clients that height measurement has started
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(JSON.stringify({ message: "startHeight" }));
        }
    });

    res.json({ message: "Height capturing started!" });
});

// Handle Kinect height updates
app.get("/heightUpdated", (req, res) => {
    const { patientId, height } = req.query;

    if (!patientId || !height) {
        return res.status(400).json({ error: "Missing patientId or height" });
    }

    console.log(✅ Height updated for Patient ${patientId}: ${height} meters);

    // Notify all WebSocket clients about height update
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(JSON.stringify({ patientId, height }));
        }
    });

    res.json({ message: "Height update received!" });
});

// Stop Kinect measurement
app.get("/stopHeight", (req, res) => {
    console.log("🛑 Stopping Kinect...");

    // Notify all WebSocket clients that Kinect has stopped
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(JSON.stringify({ message: "stopHeight" }));
        }
    });

    res.json({ message: "Kinect stopped successfully!" });
});

// Start HTTP & WebSocket server
const server = app.listen(PORT, () => {
    console.log(🚀 Server running on http://localhost:${PORT});
});

// Upgrade HTTP to WebSocket
server.on("upgrade", (request, socket, head) => {
    wss.handleUpgrade(request, socket, head, (ws) => {
        wss.emit("connection", ws, request);
    });
});

// Handle unexpected errors
process.on("uncaughtException", (err) => {
    console.error("🔥 Uncaught Exception:", err);
});

process.on("unhandledRejection", (reason, promise) => {
    console.error("🛑 Unhandled Rejection at:", promise, "reason:", reason);
});
