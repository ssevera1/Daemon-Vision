package com.daemon.vision.companion;

import android.Manifest;
import android.content.pm.PackageManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.format.Formatter;
import android.util.Log;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Main activity for the Daemon Vision Companion app.
 * <p>
 * This app runs on a standard Android phone and acts as a sensor relay for
 * AR glasses running the D-Space application. It provides:
 * <ul>
 *   <li>GPS location relay via UDP</li>
 *   <li>Auto-discovery of glasses via UDP broadcast beacons</li>
 *   <li>Connection status monitoring</li>
 *   <li>Peer count display</li>
 * </ul>
 */
public class CompanionActivity extends AppCompatActivity implements LocationListener {

    private static final String TAG = "DaemonVision.Companion";

    // Network constants
    private static final int RELAY_PORT = 7770;
    private static final int DISCOVERY_PORT = 7771;
    private static final String BEACON_PREFIX = "DSPACE:";
    private static final int BEACON_LISTEN_TIMEOUT_MS = 5000;
    private static final int GPS_RELAY_INTERVAL_MS = 500;

    // Permission request codes
    private static final int PERMISSION_REQUEST_CODE = 1001;

    // ── UI Elements ──
    private View connectionIndicator;
    private TextView connectionStatusText;
    private TextView latitudeText;
    private TextView longitudeText;
    private TextView altitudeText;
    private TextView accuracyText;
    private TextView peerCountText;
    private TextView lastRelayText;
    private TextView networkStatusText;
    private TextView localIpText;
    private EditText glassesIpInput;
    private Button startRelayButton;
    private Button stopRelayButton;
    private Button scanButton;

    // ── State ──
    private final AtomicBoolean isRelaying = new AtomicBoolean(false);
    private final AtomicBoolean isScanning = new AtomicBoolean(false);
    private volatile boolean isConnected = false;
    private volatile double currentLat = 0.0;
    private volatile double currentLon = 0.0;
    private volatile double currentAlt = 0.0;
    private volatile float currentAccuracy = 0.0f;
    private volatile int peerCount = 0;
    private volatile long lastRelayTimestamp = 0;

    private LocationManager locationManager;
    private ExecutorService networkExecutor;
    private Handler uiHandler;
    private DatagramSocket relaySocket;
    private Runnable relayRunnable;

    // ──────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Dark status bar and navigation bar
        Window window = getWindow();
        window.addFlags(WindowManager.LayoutParams.FLAG_DRAWS_SYSTEM_BAR_BACKGROUNDS);
        window.setStatusBarColor(0xFF0A0A14);
        window.setNavigationBarColor(0xFF0A0A14);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            window.getDecorView().setSystemUiVisibility(0); // Clear light flags for dark theme
        }

        setContentView(R.layout.activity_companion);

        // Initialize core services
        uiHandler = new Handler(Looper.getMainLooper());
        networkExecutor = Executors.newFixedThreadPool(3);
        locationManager = (LocationManager) getSystemService(LOCATION_SERVICE);

        // Bind UI elements
        bindViews();
        setupListeners();

        // Request permissions
        requestRequiredPermissions();

        // Display local IP
        updateLocalIpDisplay();

        Log.i(TAG, "CompanionActivity created.");
    }

    @Override
    protected void onResume() {
        super.onResume();
        startLocationUpdates();
        startUiUpdateLoop();
    }

    @Override
    protected void onPause() {
        super.onPause();
        // Keep relay running in background, but stop UI updates
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        stopRelay();
        if (networkExecutor != null && !networkExecutor.isShutdown()) {
            networkExecutor.shutdownNow();
        }
        if (locationManager != null) {
            locationManager.removeUpdates(this);
        }
        Log.i(TAG, "CompanionActivity destroyed.");
    }

    // ──────────────────────────────────────────────────────────
    // View Binding
    // ──────────────────────────────────────────────────────────

    private void bindViews() {
        connectionIndicator = findViewById(R.id.connection_indicator);
        connectionStatusText = findViewById(R.id.connection_status_text);
        latitudeText = findViewById(R.id.latitude_text);
        longitudeText = findViewById(R.id.longitude_text);
        altitudeText = findViewById(R.id.altitude_text);
        accuracyText = findViewById(R.id.accuracy_text);
        peerCountText = findViewById(R.id.peer_count_text);
        lastRelayText = findViewById(R.id.last_relay_text);
        networkStatusText = findViewById(R.id.network_status_text);
        localIpText = findViewById(R.id.local_ip_text);
        glassesIpInput = findViewById(R.id.glasses_ip_input);
        startRelayButton = findViewById(R.id.btn_start_relay);
        stopRelayButton = findViewById(R.id.btn_stop_relay);
        scanButton = findViewById(R.id.btn_scan_glasses);
    }

    private void setupListeners() {
        startRelayButton.setOnClickListener(v -> startRelay());
        stopRelayButton.setOnClickListener(v -> stopRelay());
        scanButton.setOnClickListener(v -> scanForGlasses());
    }

    // ──────────────────────────────────────────────────────────
    // Permissions
    // ──────────────────────────────────────────────────────────

    private void requestRequiredPermissions() {
        List<String> permissionsNeeded = new ArrayList<>();

        // Location
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION)
                != PackageManager.PERMISSION_GRANTED) {
            permissionsNeeded.add(Manifest.permission.ACCESS_FINE_LOCATION);
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_COARSE_LOCATION)
                != PackageManager.PERMISSION_GRANTED) {
            permissionsNeeded.add(Manifest.permission.ACCESS_COARSE_LOCATION);
        }

        // Camera (for potential future QR code pairing)
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
                != PackageManager.PERMISSION_GRANTED) {
            permissionsNeeded.add(Manifest.permission.CAMERA);
        }

        // Bluetooth
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_SCAN)
                    != PackageManager.PERMISSION_GRANTED) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH_SCAN);
            }
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT)
                    != PackageManager.PERMISSION_GRANTED) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH_CONNECT);
            }
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.NEARBY_WIFI_DEVICES)
                    != PackageManager.PERMISSION_GRANTED) {
                permissionsNeeded.add(Manifest.permission.NEARBY_WIFI_DEVICES);
            }
        } else {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH)
                    != PackageManager.PERMISSION_GRANTED) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH);
            }
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_ADMIN)
                    != PackageManager.PERMISSION_GRANTED) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH_ADMIN);
            }
        }

        // Background location (for continued relay)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_BACKGROUND_LOCATION)
                    != PackageManager.PERMISSION_GRANTED) {
                // Request background location separately after foreground is granted
                permissionsNeeded.add(Manifest.permission.ACCESS_BACKGROUND_LOCATION);
            }
        }

        if (!permissionsNeeded.isEmpty()) {
            ActivityCompat.requestPermissions(this,
                    permissionsNeeded.toArray(new String[0]),
                    PERMISSION_REQUEST_CODE);
        } else {
            onAllPermissionsGranted();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions,
                                           @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PERMISSION_REQUEST_CODE) {
            boolean allGranted = true;
            for (int result : grantResults) {
                if (result != PackageManager.PERMISSION_GRANTED) {
                    allGranted = false;
                    break;
                }
            }
            if (allGranted) {
                onAllPermissionsGranted();
            } else {
                Log.w(TAG, "Some permissions were denied. Relay functionality may be limited.");
                updateConnectionStatus(false, "Permissions incomplete");
            }
        }
    }

    private void onAllPermissionsGranted() {
        Log.i(TAG, "All required permissions granted.");
        startLocationUpdates();
    }

    // ──────────────────────────────────────────────────────────
    // Location
    // ──────────────────────────────────────────────────────────

    private void startLocationUpdates() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED) {
            try {
                locationManager.requestLocationUpdates(
                        LocationManager.GPS_PROVIDER,
                        500,  // min time interval in ms
                        0.5f, // min distance in meters
                        this
                );

                // Also request network provider for faster initial fix
                if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
                    locationManager.requestLocationUpdates(
                            LocationManager.NETWORK_PROVIDER,
                            1000,
                            1.0f,
                            this
                    );
                }

                Log.i(TAG, "Location updates started.");
            } catch (SecurityException e) {
                Log.e(TAG, "Location permission error: " + e.getMessage());
            }
        }
    }

    @Override
    public void onLocationChanged(@NonNull Location location) {
        currentLat = location.getLatitude();
        currentLon = location.getLongitude();
        currentAlt = location.getAltitude();
        currentAccuracy = location.getAccuracy();

        Log.v(TAG, String.format(Locale.US,
                "Location: %.6f, %.6f, alt=%.1f, acc=%.1f",
                currentLat, currentLon, currentAlt, currentAccuracy));
    }

    @Override
    public void onProviderEnabled(@NonNull String provider) {
        Log.i(TAG, "Provider enabled: " + provider);
    }

    @Override
    public void onProviderDisabled(@NonNull String provider) {
        Log.w(TAG, "Provider disabled: " + provider);
    }

    // ──────────────────────────────────────────────────────────
    // GPS Relay
    // ──────────────────────────────────────────────────────────

    private void startRelay() {
        String targetIp = glassesIpInput.getText().toString().trim();
        if (targetIp.isEmpty()) {
            glassesIpInput.setError("Enter glasses IP address");
            return;
        }

        if (isRelaying.getAndSet(true)) {
            Log.w(TAG, "Relay already running.");
            return;
        }

        Log.i(TAG, "Starting GPS relay to " + targetIp + ":" + RELAY_PORT);
        updateConnectionStatus(true, "Relaying to " + targetIp);

        startRelayButton.setEnabled(false);
        stopRelayButton.setEnabled(true);

        networkExecutor.submit(() -> {
            try {
                relaySocket = new DatagramSocket();
                InetAddress targetAddress = InetAddress.getByName(targetIp);

                while (isRelaying.get()) {
                    // Build GPS payload: DSPACE_GPS|lat|lon|alt|accuracy|timestamp
                    String payload = String.format(Locale.US,
                            "DSPACE_GPS|%.8f|%.8f|%.4f|%.2f|%d",
                            currentLat, currentLon, currentAlt, currentAccuracy,
                            System.currentTimeMillis());

                    byte[] data = payload.getBytes();
                    DatagramPacket packet = new DatagramPacket(
                            data, data.length, targetAddress, RELAY_PORT);

                    relaySocket.send(packet);
                    lastRelayTimestamp = System.currentTimeMillis();

                    // Listen for response to get peer count
                    try {
                        byte[] responseBuffer = new byte[256];
                        DatagramPacket responsePacket = new DatagramPacket(
                                responseBuffer, responseBuffer.length);
                        relaySocket.setSoTimeout(GPS_RELAY_INTERVAL_MS);
                        relaySocket.receive(responsePacket);

                        String response = new String(responsePacket.getData(),
                                0, responsePacket.getLength());
                        parseRelayResponse(response);
                    } catch (SocketTimeoutException e) {
                        // Expected when no response within interval
                    }

                    Thread.sleep(GPS_RELAY_INTERVAL_MS);
                }
            } catch (IOException | InterruptedException e) {
                if (isRelaying.get()) {
                    Log.e(TAG, "Relay error: " + e.getMessage());
                    uiHandler.post(() -> updateConnectionStatus(false, "Relay error"));
                }
            } finally {
                if (relaySocket != null && !relaySocket.isClosed()) {
                    relaySocket.close();
                }
            }
        });
    }

    private void stopRelay() {
        if (!isRelaying.getAndSet(false)) {
            return;
        }

        Log.i(TAG, "Stopping GPS relay.");
        updateConnectionStatus(false, "Disconnected");

        if (relaySocket != null && !relaySocket.isClosed()) {
            relaySocket.close();
        }

        uiHandler.post(() -> {
            startRelayButton.setEnabled(true);
            stopRelayButton.setEnabled(false);
        });
    }

    private void parseRelayResponse(String response) {
        // Expected format: DSPACE_ACK|peerCount|timestamp
        try {
            String[] parts = response.split("\\|");
            if (parts.length >= 2 && "DSPACE_ACK".equals(parts[0])) {
                peerCount = Integer.parseInt(parts[1]);
                isConnected = true;
            }
        } catch (NumberFormatException e) {
            Log.w(TAG, "Invalid relay response: " + response);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Glasses Discovery
    // ──────────────────────────────────────────────────────────

    private void scanForGlasses() {
        if (isScanning.getAndSet(true)) {
            Log.w(TAG, "Already scanning.");
            return;
        }

        scanButton.setEnabled(false);
        scanButton.setText("Scanning...");
        networkStatusText.setText("Scanning for D-Space beacons...");

        networkExecutor.submit(() -> {
            DatagramSocket socket = null;
            try {
                socket = new DatagramSocket(null);
                socket.setReuseAddress(true);
                socket.setBroadcast(true);
                socket.bind(new InetSocketAddress(DISCOVERY_PORT));
                socket.setSoTimeout(BEACON_LISTEN_TIMEOUT_MS);

                byte[] buffer = new byte[512];
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);

                Log.i(TAG, "Listening for DSPACE beacons on port " + DISCOVERY_PORT);

                long scanStart = System.currentTimeMillis();
                String foundIp = null;

                while (System.currentTimeMillis() - scanStart < BEACON_LISTEN_TIMEOUT_MS * 2) {
                    try {
                        socket.receive(packet);
                        String message = new String(packet.getData(), 0, packet.getLength());

                        if (message.startsWith(BEACON_PREFIX)) {
                            foundIp = packet.getAddress().getHostAddress();
                            String deviceInfo = message.substring(BEACON_PREFIX.length());
                            Log.i(TAG, "Found D-Space glasses at " + foundIp + " (" + deviceInfo + ")");

                            final String ip = foundIp;
                            final String info = deviceInfo;
                            uiHandler.post(() -> {
                                glassesIpInput.setText(ip);
                                networkStatusText.setText("Found: " + info + " @ " + ip);
                            });
                            break;
                        }
                    } catch (SocketTimeoutException e) {
                        // Continue scanning
                    }
                }

                if (foundIp == null) {
                    uiHandler.post(() -> networkStatusText.setText("No D-Space glasses found on network."));
                }

            } catch (IOException e) {
                Log.e(TAG, "Scan error: " + e.getMessage());
                uiHandler.post(() -> networkStatusText.setText("Scan failed: " + e.getMessage()));
            } finally {
                if (socket != null && !socket.isClosed()) {
                    socket.close();
                }
                isScanning.set(false);
                uiHandler.post(() -> {
                    scanButton.setEnabled(true);
                    scanButton.setText("SCAN FOR GLASSES");
                });
            }
        });
    }

    // ──────────────────────────────────────────────────────────
    // UI Updates
    // ──────────────────────────────────────────────────────────

    private void updateConnectionStatus(boolean connected, String statusMessage) {
        isConnected = connected;
        uiHandler.post(() -> {
            if (connectionIndicator != null) {
                connectionIndicator.setBackgroundColor(connected ? 0xFF00E5CC : 0xFFFF3355);
            }
            if (connectionStatusText != null) {
                connectionStatusText.setText(statusMessage);
                connectionStatusText.setTextColor(connected ? 0xFF00E5CC : 0xFFFF3355);
            }
        });
    }

    private void startUiUpdateLoop() {
        Runnable uiUpdate = new Runnable() {
            @Override
            public void run() {
                updateGpsDisplay();
                updateRelayDisplay();
                uiHandler.postDelayed(this, 500);
            }
        };
        uiHandler.post(uiUpdate);
    }

    private void updateGpsDisplay() {
        if (latitudeText != null) {
            latitudeText.setText(String.format(Locale.US, "%.6f", currentLat));
        }
        if (longitudeText != null) {
            longitudeText.setText(String.format(Locale.US, "%.6f", currentLon));
        }
        if (altitudeText != null) {
            altitudeText.setText(String.format(Locale.US, "%.1f m", currentAlt));
        }
        if (accuracyText != null) {
            accuracyText.setText(String.format(Locale.US, "%.1f m", currentAccuracy));
        }
        if (peerCountText != null) {
            peerCountText.setText(String.valueOf(peerCount));
        }
    }

    private void updateRelayDisplay() {
        if (lastRelayText != null && lastRelayTimestamp > 0) {
            SimpleDateFormat sdf = new SimpleDateFormat("HH:mm:ss.SSS", Locale.US);
            lastRelayText.setText(sdf.format(new Date(lastRelayTimestamp)));
        }
    }

    @SuppressWarnings("deprecation")
    private void updateLocalIpDisplay() {
        if (localIpText != null) {
            try {
                WifiManager wifiManager = (WifiManager) getApplicationContext()
                        .getSystemService(WIFI_SERVICE);
                if (wifiManager != null && wifiManager.getConnectionInfo() != null) {
                    int ipInt = wifiManager.getConnectionInfo().getIpAddress();
                    String ipStr = Formatter.formatIpAddress(ipInt);
                    localIpText.setText("Local IP: " + ipStr);
                } else {
                    localIpText.setText("Local IP: unavailable");
                }
            } catch (Exception e) {
                localIpText.setText("Local IP: error");
            }
        }
    }
}
