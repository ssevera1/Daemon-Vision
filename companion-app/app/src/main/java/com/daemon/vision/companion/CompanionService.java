package com.daemon.vision.companion;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.IBinder;
import android.os.Bundle;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.app.ActivityCompat;
import androidx.core.app.NotificationCompat;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;

/**
 * CompanionService — Runs as a foreground service on the phone to relay
 * GPS, camera data, and biometric auth to connected AR glasses.
 *
 * For glasses without GPS (Meta Quest, XREAL), the companion phone
 * provides location data over WiFi/Bluetooth.
 *
 * Architecture inspired by the Daemon's distributed sensor network.
 */
public class CompanionService extends Service implements LocationListener {

    private static final String TAG = "DaemonCompanion";
    private static final String CHANNEL_ID = "daemon_companion_channel";
    private static final int NOTIFICATION_ID = 7733;
    private static final int GPS_RELAY_PORT = 7735;
    private static final int GPS_UPDATE_INTERVAL_MS = 2000;
    private static final float GPS_MIN_DISTANCE_M = 0.5f;

    private LocationManager locationManager;
    private DatagramSocket relaySocket;
    private InetAddress glassesAddress;
    private int glassesPort = GPS_RELAY_PORT;
    private volatile boolean isRunning = false;

    private double lastLatitude;
    private double lastLongitude;
    private double lastAltitude;
    private float lastAccuracy;
    private float lastBearing;

    @Override
    public void onCreate() {
        super.onCreate();
        Log.i(TAG, "Daemon Vision Companion starting...");

        createNotificationChannel();
        startForeground(NOTIFICATION_ID, buildNotification("D-Space Companion Active"));

        locationManager = (LocationManager) getSystemService(Context.LOCATION_SERVICE);

        try {
            relaySocket = new DatagramSocket();
        } catch (Exception e) {
            Log.e(TAG, "Failed to create relay socket: " + e.getMessage());
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String action = intent.getAction();
            if ("START_RELAY".equals(action)) {
                String targetAddress = intent.getStringExtra("glasses_address");
                int targetPort = intent.getIntExtra("glasses_port", GPS_RELAY_PORT);
                startRelay(targetAddress, targetPort);
            } else if ("STOP_RELAY".equals(action)) {
                stopRelay();
            }
        }
        return START_STICKY;
    }

    private void startRelay(String address, int port) {
        try {
            glassesAddress = InetAddress.getByName(address);
            glassesPort = port;

            // Start GPS updates
            if (ActivityCompat.checkSelfPermission(this,
                    Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) {
                locationManager.requestLocationUpdates(
                        LocationManager.GPS_PROVIDER,
                        GPS_UPDATE_INTERVAL_MS,
                        GPS_MIN_DISTANCE_M,
                        this);
                locationManager.requestLocationUpdates(
                        LocationManager.FUSED_PROVIDER,
                        GPS_UPDATE_INTERVAL_MS,
                        GPS_MIN_DISTANCE_M,
                        this);
            }

            isRunning = true;
            Log.i(TAG, "GPS relay started → " + address + ":" + port);
            updateNotification("Relaying GPS to glasses at " + address);

        } catch (Exception e) {
            Log.e(TAG, "Failed to start relay: " + e.getMessage());
        }
    }

    private void stopRelay() {
        isRunning = false;
        locationManager.removeUpdates(this);
        Log.i(TAG, "GPS relay stopped.");
        updateNotification("D-Space Companion Idle");
    }

    @Override
    public void onLocationChanged(@NonNull Location location) {
        lastLatitude = location.getLatitude();
        lastLongitude = location.getLongitude();
        lastAltitude = location.getAltitude();
        lastAccuracy = location.getAccuracy();
        lastBearing = location.getBearing();

        // Relay to glasses via UDP
        relayLocationToGlasses(location);
    }

    private void relayLocationToGlasses(Location location) {
        if (!isRunning || glassesAddress == null || relaySocket == null) return;

        // JSON payload matching GPSLocation struct in Unity
        String json = String.format(
                "{\"lat\":%.8f,\"lon\":%.8f,\"alt\":%.2f,\"acc\":%.1f,\"bearing\":%.1f,\"ts\":%d}",
                location.getLatitude(),
                location.getLongitude(),
                location.getAltitude(),
                location.getAccuracy(),
                location.getBearing(),
                System.currentTimeMillis() / 1000
        );

        byte[] data = ("DSPACE_GPS:" + json).getBytes(StandardCharsets.UTF_8);

        new Thread(() -> {
            try {
                DatagramPacket packet = new DatagramPacket(
                        data, data.length, glassesAddress, glassesPort);
                relaySocket.send(packet);
            } catch (IOException e) {
                Log.w(TAG, "GPS relay send failed: " + e.getMessage());
            }
        }).start();
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
