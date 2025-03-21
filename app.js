// Import Firebase SDK
import { initializeApp } from "https://www.gstatic.com/firebasejs/10.7.1/firebase-app.js";
import { getDatabase, ref, set, get, update, onValue } from "https://www.gstatic.com/firebasejs/10.7.1/firebase-database.js";

// 🔹 Firebase Config
const firebaseConfig = {
    apiKey: "AIzaSyAcXHU2f-3sl2IA66SoIeftziwhCAFf8YE",
    authDomain: "databasec-b35a7.firebaseapp.com",
    projectId: "databasec-b35a7",
    storageBucket: "databasec-b35a7.appspot.com",
    messagingSenderId: "562329529929",
    appId: "1:562329529929:web:daaa9c7b19c33f05c45998",
    measurementId: "G-M7E8W43FCD"
};

// Initialize Firebase
const app = initializeApp(firebaseConfig);
const db = getDatabase(app);
let patientID = null;

// 🔹 Generate Unique Patient ID
function generatePatientID(name, age, gender) {
    if (!name || !age || !gender) return null;
    const genderCode = gender.charAt(0).toUpperCase();
    const ageCode = String(age).padStart(2, '0');
    const nameCode = name.substring(0, 2).toUpperCase();
    return ${genderCode}${ageCode}${nameCode};
}

// 🔹 Real-time Height Listener
function listenForHeightUpdates() {
    const patientRef = ref(db, "patients/");
    onValue(patientRef, (snapshot) => {
        if (snapshot.exists()) {
            snapshot.forEach((childSnapshot) => {
                const patient = childSnapshot.val();
                if (patient.height) {
                    document.getElementById("height").value = patient.height;
                    document.getElementById("height").dispatchEvent(new Event("input"));
                    validateForm();
                }
            });
        }
    });
}
listenForHeightUpdates();

// 🔹 WebSocket Connection for Real-time Updates
let socket = null;
function connectWebSocket() {
    socket = new WebSocket("ws://localhost:5000");

    socket.onopen = () => console.log("✅ WebSocket Connected!");
    
    socket.onmessage = (event) => {
        try {
            let data = JSON.parse(event.data);
            console.log("📩 WebSocket Data Received:", data);
            if (data.height && data.patientId) {
                document.getElementById("height").value = data.height;
                document.getElementById("height").dispatchEvent(new Event("input"));
                validateForm();
            }
        } catch (error) {
            console.error("🔴 WebSocket Data Error:", error);
        }
    };

    socket.onerror = (error) => console.error("🔴 WebSocket Error:", error);
    socket.onclose = () => {
        console.log("⚠ WebSocket Disconnected! Reconnecting in 3s...");
        setTimeout(connectWebSocket, 3000);
    };
}
connectWebSocket();

// 🔹 Start Kinect Height Capture
async function startKinectCapture() {
    try {
        let response = await fetch("http://localhost:5000/startHeight");
        if (!response.ok) throw new Error("Failed to start Kinect");
        alert("📏 Kinect started! Waiting for height...");
    } catch (error) {
        console.error("❌ Kinect Start Error:", error);
        alert("❌ Kinect height capture failed.");
    }
}

// 🔹 Stop Kinect Manually
async function stopKinect() {
    try {
        let response = await fetch("http://localhost:5000/stopHeight"); 
        if (!response.ok) throw new Error("Failed to stop Kinect");
        alert("🛑 Kinect stopped.");
    } catch (error) {
        console.error("❌ Kinect Stop Error:", error);
        alert("❌ Kinect stop failed.");
    }
}

// 🔹 Handle Form Submission
document.getElementById("patientForm").addEventListener("submit", async function (event) {
    event.preventDefault();

    const name = document.getElementById("name").value.trim();
    const age = document.getElementById("age").value.trim();
    const gender = document.getElementById("gender").value;
    const height = document.getElementById("height").value;

    if (!name || !age || !gender || !height) {
        alert("⚠ Please fill all fields before submitting.");
        return;
    }

    patientID = generatePatientID(name, age, gender);
    if (!patientID) {
        alert("❌ Error generating Patient ID.");
        return;
    }

    const patientRef = ref(db, "patients/" + patientID);

    try {
        const snapshot = await get(patientRef);

        if (snapshot.exists()) {
            await update(patientRef, { name, age, gender, height });
            alert(✅ Patient ${name} updated successfully!);
        } else {
            await set(patientRef, { name, age, gender, height });
            alert(✅ Patient ${name} added successfully!);
        }

        document.getElementById("patientForm").reset();
        document.getElementById("submitBtn").disabled = true;
        fetchPatients();
    } catch (error) {
        alert(❌ Error: ${error.message});
        console.error("Error:", error);
    }
});

// 🔹 Fetch & Update Patient List in Real-time
function fetchPatients() {
    const patientRef = ref(db, "patients");
    onValue(patientRef, (snapshot) => {
        const patientList = document.getElementById("patientList");
        patientList.innerHTML = "";

        if (snapshot.exists()) {
            snapshot.forEach((childSnapshot) => {
                const id = childSnapshot.key;
                const patient = childSnapshot.val();
                const listItem = document.createElement("li");
                listItem.textContent = ${id}: ${patient.name}, ${patient.age} years, ${patient.gender}, ${patient.height} cm;
                patientList.appendChild(listItem);
            });
        }
    });
}

// 🔹 Validate Form to Enable Submit Button
function validateForm() {
    const name = document.getElementById("name").value.trim();
    const age = document.getElementById("age").value.trim();
    const gender = document.getElementById("gender").value;
    const height = document.getElementById("height").value;

    document.getElementById("submitBtn").disabled = !(name && age && gender && height);
}

// 🔹 Attach Event Listeners
document.getElementById("captureHeightBtn").addEventListener("click", startKinectCapture);
document.getElementById("stopKinectBtn").addEventListener("click", stopKinect);
document.getElementById("name").addEventListener("input", validateForm);
document.getElementById("age").addEventListener("input", validateForm);
document.getElementById("gender").addEventListener("change", validateForm);

// 🔹 Fetch Patients on Page Load
fetchPatients();
