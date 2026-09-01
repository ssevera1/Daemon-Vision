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
import java.nio.charset.StandardCharsets;
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
 *   <li>GPS location relay via UDP (port {@link RelayProtocol#GPS_RELAY_PORT})</li>
 *   <li>Auto-discovery of glasses from their D-Space beacons (port {@link RelayProtocol#DISCOVERY_PORT})</li>
 *   <li>Connection status monitoring</li>
 *   <li>Peer count display, echoed back by the glasses in each ACK</li>
 * </ul>
 */
public class CompanionActivity extends AppCompatActivity implements LocationListener {

    private static final String TAG = "DaemonVision.Companion";

    private static final int BEACON_LISTEN_TIMEOUT_MS = 5000;
    private static final int GPS_RELAY_INTERVAL_MS = 500;
    private static final int UI_REFRESH_MS = 500;

    // Permission request codes
    private static final int PERMISSION_REQUEST_CODE = 1001;
    private static final int BACKGROUND_LOCATION_REQUEST_CODE = 1002;

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
    private volatile float currentBearing = 0.0f;
    private volatile int peerCount = 0;
    private volatile long lastRelayTimestamp = 0;
    private volatile boolean locationUpdatesActive = false;

    private LocationManager locationManager;
    private ExecutorService networkExecutor;
    private Handler uiHandler;
    private DatagramSocket relaySocket;
    private final Runnable uiRefresh = new Runnable() {
        @Override
        public void run() {
            updateGpsDisplay();
            updateRelayDisplay();
            uiHandler.postDelayed(this, UI_REFRESH_MS);
        }
    };

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

        setContentView(R.layout.activity_companion);

        uiHandler = new Handler(Looper.getMainLooper());
        networkExecutor = Executors.newFixedThreadPool(3);
        locationManager = (LocationManager) getSystemService(LOCATION_SERVICE);

        bindViews();
        setupListeners();
        requestRequiredPermissions();
        updateLocalIpDisplay();

        Log.i(TAG, "CompanionActivity created.");
    }

    @Override
    protected void onResume() {
        super.onResume();
        startLocationUpdates();
        uiHandler.removeCallbacks(uiRefresh);
        uiHandler.post(uiRefresh);
        updateLocalIpDisplay();
    }

    @Override
    protected void onPause() {
        super.onPause();
        // The relay thread keeps running; only the screen refresh stops.
        uiHandler.removeCallbacks(uiRefresh);
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        stopRelay();
        uiHandler.removeCallbacksAndMessages(null);
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

    private boolean hasPermission(String permission) {
        return ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestRequiredPermissions() {
        List<String> permissionsNeeded = new ArrayList<>();

        if (!hasPermission(Manifest.permission.ACCESS_FINE_LOCATION)) {
            permissionsNeeded.add(Manifest.permission.ACCESS_FINE_LOCATION);
        }
        if (!hasPermission(Manifest.permission.ACCESS_COARSE_LOCATION)) {
            permissionsNeeded.add(Manifest.permission.ACCESS_COARSE_LOCATION);
        }

        // Camera (for future QR code pairing)
        if (!hasPermission(Manifest.permission.CAMERA)) {
            permissionsNeeded.add(Manifest.permission.CAMERA);
        }

        // Bluetooth / nearby devices
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (!hasPermission(Manifest.permission.BLUETOOTH_SCAN)) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH_SCAN);
            }
            if (!hasPermission(Manifest.permission.BLUETOOTH_CONNECT)) {
                permissionsNeeded.add(Manifest.permission.BLUETOOTH_CONNECT);
            }
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (!hasPermission(Manifest.permission.NEARBY_WIFI_DEVICES)) {
                permissionsNeeded.add(Manifest.permission.NEARBY_WIFI_DEVICES);
            }
            if (!hasPermission(Manifest.permission.POST_NOTIFICATIONS)) {
                permissionsNeeded.add(Manifest.permission.POST_NOTIFICATIONS);
            }
        }

        // Background location must be requested on its own after foreground
        // location is granted. On Android 11+ a combined request is silently
        // ignored by the system, so it is handled in onRequestPermissionsResult.

        if (!permissionsNeeded.isEmpty()) {
            ActivityCompat.requestPermissions(this,
                    permissionsNeeded.toArray(new String[0]),
                    PERMISSION_REQUEST_CODE);
        } else {
            onForegroundPermissionsGranted();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions,
                                           @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == PERMISSION_REQUEST_CODE) {
            if (hasPermission(Manifest.permission.ACCESS_FINE_LOCATION)) {
                onForegroundPermissionsGranted();
            } else {
                Log.w(TAG, "Fine location was denied. GPS relay cannot run.");
                updateConnectionStatus(false, "Location permission required");
            }
        } else if (requestCode == BACKGROUND_LOCATION_REQUEST_CODE) {
            boolean granted = grantResults.length > 0
                    && grantResults[0] == PackageManager.PERMISSION_GRANTED;
            Log.i(TAG, granted
                    ? "Background location granted; relay continues with the screen off."
                    : "Background location denied; relay pauses when the app is not visible.");
        }
    }

    private void onForegroundPermissionsGranted() {
        Log.i(TAG, "Foreground permissions granted.");
        startLocationUpdates();

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                && !hasPermission(Manifest.permission.ACCESS_BACKGROUND_LOCATION)) {
            ActivityCompat.requestPermissions(this,
                    new String[]{Manifest.permission.ACCESS_BACKGROUND_LOCATION},
                    BACKGROUND_LOCATION_REQUEST_CODE);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Location
    // ──────────────────────────────────────────────────────────

    private void startLocationUpdates() {
        if (locationUpdatesActive) return;
        if (!hasPermission(Manifest.permission.ACCESS_FINE_LOCATION)) return;

        try {
            locationManager.requestLocationUpdates(
                    LocationManager.GPS_PROVIDER,
                    500,  // min time interval in ms
                    0.5f, // min distance in meters
                    this
            );

            // Also request the network provider for a faster initial fix
            if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
                locationManager.requestLocationUpdates(
                        LocationManager.NETWORK_PROVIDER,
                        1000,
                        1.0f,
                        this
                );
            }

            locationUpdatesActive = true;
            Log.i(TAG, "Location updates started.");
        } catch (SecurityException e) {
            Log.e(TAG, "Location permission error: " + e.getMessage());
        }
    }

    @Override
    public void onLocationChanged(@NonNull Location location) {
        currentLat = location.getLatitude();
        currentLon = location.getLongitude();
        currentAlt = location.getAltitude();
        currentAccuracy = location.getAccuracy();
        if (location.hasBearing()) {
            currentBearing = location.getBearing();
        }

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
        final String targetIp = glassesIpInput.getText().toString().trim();
        if (targetIp.isEmpty()) {
            glassesIpInput.setError("Enter glasses IP address");
            return;
        }

        if (isRelaying.getAndSet(true)) {
            Log.w(TAG, "Relay already running.");
            return;
        }

        Log.i(TAG, "Starting GPS relay to " + targetIp + ":" + RelayProtocol.GPS_RELAY_PORT);
        updateConnectionStatus(false, "Connecting to " + targetIp);

        startRelayButton.setEnabled(false);
        stopRelayButton.setEnabled(true);

        networkExecutor.submit(() -> {
            DatagramSocket socket = null;
            try {
                socket = new DatagramSocket();
                socket.setSoTimeout(GPS_RELAY_INTERVAL_MS);
                relaySocket = socket;
                InetAddress targetAddress = InetAddress.getByName(targetIp);

                while (isRelaying.get()) {
                    String payload = RelayProtocol.buildGpsPacket(
                            currentLat, currentLon, currentAlt, currentAccuracy,
                            currentBearing, System.currentTimeMillis());

                    byte[] data = payload.getBytes(StandardCharsets.UTF_8);
                    socket.send(new DatagramPacket(data, data.length, targetAddress,
                            RelayProtocol.GPS_RELAY_PORT));
                    lastRelayTimestamp = System.currentTimeMillis();

                    // The glasses answer every fix with an ACK carrying the mesh peer count.
                    try {
                        byte[] responseBuffer = new byte[256];
                        DatagramPacket responsePacket = new DatagramPacket(
                                responseBuffer, responseBuffer.length);
                        socket.receive(responsePacket);

                        String response = new String(responsePacket.getData(),
                                0, responsePacket.getLength(), StandardCharsets.UTF_8);
                        int peers = RelayProtocol.parseAckPeerCount(response);
                        if (peers >= 0) {
                            peerCount = peers;
                            if (!isConnected) {
                                updateConnectionStatus(true, "Relaying to " + targetIp);
                            }
                        }
                    } catch (SocketTimeoutException e) {
                        // No ACK this round; the glasses may be busy or out of range.
                    }

                    Thread.sleep(GPS_RELAY_INTERVAL_MS);
                }
            } catch (IOException | InterruptedException e) {
                if (isRelaying.get()) {
                    Log.e(TAG, "Relay error: " + e.getMessage());
                    uiHandler.post(() -> updateConnectionStatus(false, "Relay error"));
                }
            } finally {
                if (socket != null && !socket.isClosed()) {
                    socket.close();
                }
                relaySocket = null;
            }
        });
    }

    private void stopRelay() {
        if (!isRelaying.getAndSet(false)) {
            return;
        }

        Log.i(TAG, "Stopping GPS relay.");
        updateConnectionStatus(false, "Disconnected");

        DatagramSocket socket = relaySocket;
        if (socket != null && !socket.isClosed()) {
            socket.close();
        }

        uiHandler.post(() -> {
            startRelayButton.setEnabled(true);
            stopRelayButton.setEnabled(false);
        });
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
                socket.bind(new InetSocketAddress(RelayProtocol.DISCOVERY_PORT));
                socket.setSoTimeout(BEACON_LISTEN_TIMEOUT_MS);

                byte[] buffer = new byte[1024];
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);

                Log.i(TAG, "Listening for DSPACE beacons on port " + RelayProtocol.DISCOVERY_PORT);

                long scanStart = System.currentTimeMillis();
                String foundIp = null;

                while (System.currentTimeMillis() - scanStart < BEACON_LISTEN_TIMEOUT_MS * 2) {
                    try {
                        socket.receive(packet);
                        String message = new String(packet.getData(), 0, packet.getLength(),
                                StandardCharsets.UTF_8);

                        if (message.startsWith(RelayProtocol.BEACON_PREFIX)) {
                            foundIp = packet.getAddress().getHostAddress();
                            String deviceInfo = message.substring(RelayProtocol.BEACON_PREFIX.length());
                            Log.i(TAG, "Found D-Space glasses at " + foundIp + " (" + deviceInfo + ")");

                            final String ip = foundIp;
                            final String info = summarizeBeacon(deviceInfo);
                            uiHandler.post(() -> {
                                glassesIpInput.setText(ip);
                                networkStatusText.setText("Found: " + info + " @ " + ip);
                            });
                            break;
                        }
                    } catch (SocketTimeoutException e) {
                        // Keep listening until the overall scan window closes
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

    /** Pull the callsign out of the beacon JSON without a JSON dependency. */
    private static String summarizeBeacon(String beaconJson) {
        int idx = beaconJson.indexOf("\"Callsign\":\"");
        if (idx < 0) return "D-Space";
        int start = idx + "\"Callsign\":\"".length();
        int end = beaconJson.indexOf('"', start);
        return end > start ? beaconJson.substring(start, end) : "D-Space";
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
        if (localIpText == null) return;

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
