package com.daemon.vision.companion;

import android.location.Location;

import java.util.Locale;

/**
 * Wire format shared with the Unity app. Keep in sync with
 * unity-project/Assets/Scripts/Spatial/CompanionLocationReceiver.cs and
 * unity-project/Assets/Scripts/Network/PeerDiscovery.cs; tools/ci/validate_project.py
 * checks that the port numbers and prefixes on both sides still agree.
 */
public final class RelayProtocol {

    /** UDP port the glasses listen on for GPS fixes (CompanionLocationReceiver.DefaultPort). */
    public static final int GPS_RELAY_PORT = 7735;

    /** UDP port the glasses broadcast discovery beacons on (PeerDiscovery.DefaultDiscoveryPort). */
    public static final int DISCOVERY_PORT = 7734;

    public static final String GPS_PREFIX = "DSPACE_GPS";
    public static final String ACK_PREFIX = "DSPACE_ACK";
    public static final String BEACON_PREFIX = "DSPACE:";
    public static final String SEPARATOR = "|";

    private RelayProtocol() {}

    /** DSPACE_GPS|lat|lon|alt|accuracy|bearing|unixMillis */
    public static String buildGpsPacket(Location location) {
        return buildGpsPacket(
                location.getLatitude(),
                location.getLongitude(),
                location.getAltitude(),
                location.getAccuracy(),
                location.getBearing(),
                System.currentTimeMillis());
    }

    public static String buildGpsPacket(double lat, double lon, double alt,
                                        float accuracy, float bearing, long unixMillis) {
        return String.format(Locale.US,
                "%s|%.8f|%.8f|%.4f|%.2f|%.1f|%d",
                GPS_PREFIX, lat, lon, alt, accuracy, bearing, unixMillis);
    }

    /**
     * Parse "DSPACE_ACK|peerCount|unixMillis". Returns the peer count, or -1 when
     * the text is not an ACK.
     */
    public static int parseAckPeerCount(String response) {
        if (response == null) return -1;
        String[] parts = response.trim().split("\\|");
        if (parts.length < 2 || !ACK_PREFIX.equals(parts[0])) return -1;
        try {
            return Integer.parseInt(parts[1]);
        } catch (NumberFormatException e) {
            return -1;
        }
    }
}
