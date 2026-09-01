package com.daemon.vision.companion;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Build;
import android.os.Bundle;
import android.os.IBinder;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.app.ActivityCompat;
import androidx.core.app.NotificationCompat;
import androidx.core.app.ServiceCompat;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * CompanionService: runs as a foreground service on the phone to relay GPS fixes
 * to AR glasses that have no GPS radio of their own (Meta Quest, XREAL, Magic Leap).
 * <p>
 * Every fix is sent as one UDP datagram in the format that
 * {@code CompanionLocationReceiver} on the Unity side parses:
 * <pre>DSPACE_GPS|lat|lon|alt|accuracy|bearing|unixMillis</pre>
 */
public class CompanionService extends Service implements LocationListener {

    private static final String TAG = "DaemonCompanion";
    private static final String CHANNEL_ID = "daemon_companion_channel";
    private static final int NOTIFICATION_ID = 7733;
    private static final int GPS_UPDATE_INTERVAL_MS = 2000;
    private static final float GPS_MIN_DISTANCE_M = 0.5f;

    public static final String ACTION_START_RELAY = "START_RELAY";
    public static final String ACTION_STOP_RELAY = "STOP_RELAY";
    public static final String EXTRA_GLASSES_ADDRESS = "glasses_address";
    public static final String EXTRA_GLASSES_PORT = "glasses_port";

    private LocationManager locationManager;
    private DatagramSocket relaySocket;
    private ExecutorService sendExecutor;
    private volatile InetAddress glassesAddress;
    private volatile int glassesPort = RelayProtocol.GPS_RELAY_PORT;
    private volatile boolean isRunning = false;

    @Override
    public void onCreate() {
        super.onCreate();
        Log.i(TAG, "Daemon Vision Companion starting...");

        createNotificationChannel();
        startAsForeground(buildNotification("D-Space Companion Active"));

        locationManager = (LocationManager) getSystemService(Context.LOCATION_SERVICE);
        sendExecutor = Executors.newSingleThreadExecutor();

        try {
            relaySocket = new DatagramSocket();
        } catch (Exception e) {
            Log.e(TAG, "Failed to create relay socket: " + e.getMessage());
        }
    }

    /**
     * Android 14 requires the foreground service type to be passed at start time,
     * not only declared in the manifest, or the service is killed with
     * MissingForegroundServiceTypeException.
     */
    private void startAsForeground(Notification notification) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                ? ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION
                : 0;
        ServiceCompat.startForeground(this, NOTIFICATION_ID, notification, type);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String action = intent.getAction();
            if (ACTION_START_RELAY.equals(action)) {
                String targetAddress = intent.getStringExtra(EXTRA_GLASSES_ADDRESS);
                int targetPort = intent.getIntExtra(EXTRA_GLASSES_PORT, RelayProtocol.GPS_RELAY_PORT);
                startRelay(targetAddress, targetPort);
            } else if (ACTION_STOP_RELAY.equals(action)) {
                stopRelay();
            }
        }
        return START_STICKY;
    }

    private void startRelay(String address, int port) {
        if (address == null || address.trim().isEmpty()) {
            Log.w(TAG, "startRelay called without a glasses address");
            return;
        }

        try {
            glassesAddress = InetAddress.getByName(address.trim());
            glassesPort = port;

            if (ActivityCompat.checkSelfPermission(this,
                    Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) {
                locationManager.requestLocationUpdates(
                        LocationManager.GPS_PROVIDER,
                        GPS_UPDATE_INTERVAL_MS,
                        GPS_MIN_DISTANCE_M,
                        this);

                // The fused provider only exists on Android 12+.
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
                        && locationManager.isProviderEnabled(LocationManager.FUSED_PROVIDER)) {
                    locationManager.requestLocationUpdates(
                            LocationManager.FUSED_PROVIDER,
                            GPS_UPDATE_INTERVAL_MS,
                            GPS_MIN_DISTANCE_M,
                            this);
                }
            } else {
                Log.w(TAG, "Fine location permission missing; relay will start once granted");
            }

            isRunning = true;
            Log.i(TAG, "GPS relay started -> " + address + ":" + port);
            updateNotification("Relaying GPS to glasses at " + address);

        } catch (Exception e) {
            Log.e(TAG, "Failed to start relay: " + e.getMessage());
        }
    }

    private void stopRelay() {
        isRunning = false;
        if (locationManager != null) {
            locationManager.removeUpdates(this);
        }
        Log.i(TAG, "GPS relay stopped.");
        updateNotification("D-Space Companion Idle");
    }

    @Override
    public void onLocationChanged(@NonNull Location location) {
        relayLocationToGlasses(location);
    }

    private void relayLocationToGlasses(Location location) {
        if (!isRunning || glassesAddress == null || relaySocket == null || sendExecutor == null) return;

        final String payload = RelayProtocol.buildGpsPacket(location);
        final byte[] data = payload.getBytes(StandardCharsets.UTF_8);
        final InetAddress target = glassesAddress;
        final int port = glassesPort;

        sendExecutor.submit(() -> {
            try {
                relaySocket.send(new DatagramPacket(data, data.length, target, port));
            } catch (IOException e) {
                Log.w(TAG, "GPS relay send failed: " + e.getMessage());
            }
        });
    }

    private Notification buildNotification(String text) {
        return new NotificationCompat.Builder(this, CHANNEL_ID)
                .setContentTitle("Daemon Vision")
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_menu_compass)
                .setPriority(NotificationCompat.PRIORITY_LOW)
                .setOngoing(true)
                .build();
    }

    private void updateNotification(String text) {
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.notify(NOTIFICATION_ID, buildNotification(text));
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Daemon Vision Companion",
                    NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("D-Space GPS relay and companion services");
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    @Nullable
    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onDestroy() {
        stopRelay();
        if (sendExecutor != null) {
            sendExecutor.shutdownNow();
        }
        if (relaySocket != null) {
            relaySocket.close();
        }
        super.onDestroy();
    }

    // LocationListener callbacks
    @Override public void onStatusChanged(String provider, int status, Bundle extras) {}
    @Override public void onProviderEnabled(@NonNull String provider) {}
    @Override public void onProviderDisabled(@NonNull String provider) {}
}
