using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace KinectGaitAnalysis.Services
{
    public class KinectSkeletalTrackingService : IDisposable
    {
        private readonly KinectSensor _kinectSensor;
        private BodyFrameReader? _bodyFrameReader;
        private readonly FirebaseClient _firebaseClient;
        private readonly string _patientId;
        private bool _disposed;
        private readonly Dictionary<string, DateTime> _lastJointUpdateTimes;
        private const int MIN_UPDATE_INTERVAL_MS = 200; // Reduced frequency to prevent overwhelming
        private const int WEBSOCKET_SEND_INTERVAL_MS = 200;
        private const int MAX_BUFFER_SIZE = 30;
        private bool _trackingActive;
        private readonly ClientWebSocket _webSocket;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Uri _webSocketUri = new Uri("ws://localhost:5000");
        private bool _isConnected;
        private readonly Queue<Dictionary<string, object>> _messageBuffer;
        private readonly SemaphoreSlim _reconnectSemaphore;
        private Task? _sendTask;
        private Task? _receiveTask;

        public KinectSkeletalTrackingService(KinectSensor kinectSensor, string firebaseUrl, string patientId)
        {
            _kinectSensor = kinectSensor ?? throw new ArgumentNullException(nameof(kinectSensor));
            _firebaseClient = new FirebaseClient(firebaseUrl);
            _patientId = patientId ?? throw new ArgumentNullException(nameof(patientId));
            _lastJointUpdateTimes = new Dictionary<string, DateTime>();
            _messageBuffer = new Queue<Dictionary<string, object>>();
            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();
            _reconnectSemaphore = new SemaphoreSlim(1, 1);

            InitializeKinect();
            _ = ConnectWebSocketAsync(); // Start WebSocket connection
        }

        private async Task ConnectWebSocketAsync()
        {
            if (!await _reconnectSemaphore.WaitAsync(0)) // Don't wait if already reconnecting
            {
                return;
            }

            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    if (_webSocket.State != WebSocketState.None && _webSocket.State != WebSocketState.Closed)
                    {
                        _webSocket.Dispose();
                    }

                    await _webSocket.ConnectAsync(_webSocketUri, _cancellationTokenSource.Token);
                    _isConnected = true;
                    Console.WriteLine("✅ WebSocket connected successfully");

                    // Start listening for messages
                    _receiveTask = ReceiveWebSocketMessagesAsync();
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Console.WriteLine($"⚠ WebSocket connection error: {ex.Message}");
                await Task.Delay(5000); // Wait before retrying
                _ = ConnectWebSocketAsync(); // Schedule reconnection
            }
            finally
            {
                _reconnectSemaphore.Release();
            }
        }

        private async Task ReceiveWebSocketMessagesAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (_webSocket.State == WebSocketState.Open && !_disposed)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cancellationTokenSource.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            _cancellationTokenSource.Token
                        );
                        _isConnected = false;
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessWebSocketMessage(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ WebSocket receive error: {ex.Message}");
                _isConnected = false;
                _ = ConnectWebSocketAsync(); // Attempt to reconnect
            }
        }

        private void ProcessWebSocketMessage(string message)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
                if (data == null)
                {
                    Console.WriteLine("⚠ Received null or invalid JSON.");
                    return;
                }

                if (data.TryGetValue("type", out var type) && type != null)
                {
                    switch (type.ToString())
                    {
                        case "start_skeletal":
                            StartSkeletalTracking();
                            break;
                        case "stop_skeletal":
                            StopTracking();
                            break;
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"⚠ JSON parsing error: {ex.Message}");
            }
        }

        private async Task StartWebSocketSendLoop()
        {
            try
            {
                while (!_disposed && _trackingActive)
                {
                    if (_messageBuffer.Count > 0 && _isConnected)
                    {
                        var messages = new List<Dictionary<string, object>>();
                        lock (_messageBuffer)
                        {
                            while (_messageBuffer.Count > 0 && messages.Count < 10)
                            {
                                messages.Add(_messageBuffer.Dequeue());
                            }
                        }

                        if (messages.Any())
                        {
                            await SendWebSocketDataAsync(messages);
                        }
                    }
                    await Task.Delay(WEBSOCKET_SEND_INTERVAL_MS);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Send loop error: {ex.Message}");
                if (!_disposed)
                {
                    _ = StartWebSocketSendLoop(); // Restart send loop if not disposed
                }
            }
        }

        private void InitializeKinect()
        {
            try
            {
                if (!_kinectSensor.IsAvailable)
                {
                    throw new InvalidOperationException("Kinect sensor is not available");
                }

                if (!_kinectSensor.IsOpen)
                {
                    _kinectSensor.Open();
                }

                _bodyFrameReader = _kinectSensor.BodyFrameSource.OpenReader();
                if (_bodyFrameReader == null)
                {
                    throw new InvalidOperationException("Failed to initialize BodyFrameReader");
                }

                _bodyFrameReader.FrameArrived += OnBodyFrameArrived;
                Console.WriteLine("✅ Kinect skeletal tracking service initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Initialization error: {ex.Message}");
                throw;
            }
        }

        public void StartSkeletalTracking()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(KinectSkeletalTrackingService));
            }

            if (!_kinectSensor.IsAvailable)
            {
                throw new InvalidOperationException("Kinect sensor is not available");
            }

            _trackingActive = true;
            _sendTask = StartWebSocketSendLoop();
            Console.WriteLine("🦴 Skeletal tracking started.");
        }

        public void StopTracking()
        {
            if (_disposed) return;

            _trackingActive = false;
            Console.WriteLine("🛑 Skeletal tracking stopped.");
        }

        private async void OnBodyFrameArrived(object? sender, BodyFrameArrivedEventArgs e)
        {
            if (_disposed || !_trackingActive) return;

            try
            {
                using var bodyFrame = e.FrameReference?.AcquireFrame();
                if (bodyFrame == null) return;

                await ProcessBodyFrame(bodyFrame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Error processing body frame: {ex.Message}");
            }
        }

        private async Task ProcessBodyFrame(BodyFrame bodyFrame)
        {
            Body[] bodies = new Body[bodyFrame.BodyCount];
            bodyFrame.GetAndRefreshBodyData(bodies);

            var trackedBodies = bodies.Where(b => b.IsTracked).ToList();
            if (!trackedBodies.Any())
            {
                return;
            }

            var timestamp = DateTime.UtcNow;
            var batchData = new Dictionary<string, object>();

            foreach (var body in trackedBodies)
            {
                var jointData = await ProcessBodyJoints(body, timestamp);
                if (jointData.Any())
                {
                    batchData[Guid.NewGuid().ToString()] = jointData;
                }
            }

            if (batchData.Any())
            {
                await SaveSkeletalDataAsync(batchData);
            }
        }

        private Task<Dictionary<string, object>> ProcessBodyJoints(Body body, DateTime timestamp)
        {
            var jointData = new Dictionary<string, object>();

            foreach (var joint in body.Joints)
            {
                string jointKey = joint.Key.ToString();
                
                if (!ShouldUpdateJoint(jointKey))
                {
                    continue;
                }

                jointData[jointKey] = new
                {
                    X = Math.Round(joint.Value.Position.X, 4),
                    Y = Math.Round(joint.Value.Position.Y, 4),
                    Z = Math.Round(joint.Value.Position.Z, 4),
                    TrackingState = joint.Value.TrackingState.ToString(),
                    Timestamp = timestamp
                };

                _lastJointUpdateTimes[jointKey] = timestamp;
            }

            return Task.FromResult(jointData);
        }

        private bool ShouldUpdateJoint(string jointKey)
        {
            if (!_lastJointUpdateTimes.TryGetValue(jointKey, out DateTime lastUpdate))
            {
                return true;
            }

            return (DateTime.UtcNow - lastUpdate).TotalMilliseconds >= MIN_UPDATE_INTERVAL_MS;
        }

        private async Task SaveSkeletalDataAsync(Dictionary<string, object> batchData)
        {
            try
            {
                // Send to Firebase
                var firebaseTask = _firebaseClient
                    .Child("patients")
                    .Child(_patientId)
                    .Child("SkeletalData")
                    .PatchAsync(batchData);

                // Buffer for WebSocket
                BufferMessage(batchData);

                await firebaseTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Data upload error: {ex.Message}");
            }
        }

        private void BufferMessage(Dictionary<string, object> data)
        {
            lock (_messageBuffer)
            {
                if (_messageBuffer.Count < MAX_BUFFER_SIZE)
                {
                    _messageBuffer.Enqueue(data);
                }
            }
        }

        private async Task SendWebSocketDataAsync(List<Dictionary<string, object>> dataList)
        {
            if (!_isConnected || _webSocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                var message = JsonSerializer.Serialize(new
                {
                    type = "skeletal_data",
                    patientId = _patientId,
                    timestamp = DateTime.UtcNow,
                    data = dataList
                });

                var bytes = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource.Token
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket send error: {ex.Message}");
                _isConnected = false;
                _ = ConnectWebSocketAsync(); // Attempt to reconnect
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _trackingActive = false;
                _disposed = true;

                if (_bodyFrameReader != null)
                {
                    _bodyFrameReader.FrameArrived -= OnBodyFrameArrived;
                    _bodyFrameReader.Dispose();
                }

                _cancellationTokenSource.Cancel();
                
                if (_webSocket.State == WebSocketState.Open)
                {
                    _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Service shutting down",
                        CancellationToken.None
                    ).Wait();
                }
                
                _webSocket.Dispose();
                _cancellationTokenSource.Dispose();
                _reconnectSemaphore.Dispose();

                Console.WriteLine("🔄 Kinect tracking service disposed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during disposal: {ex.Message}");
            }
        }
    }
}
