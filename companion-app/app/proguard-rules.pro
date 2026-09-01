# ProGuard rules for the D-Space companion app.
# Keep the LocationListener callbacks and the biometric bridge reachable by name.
-keep class com.daemon.vision.companion.BiometricBridge { *; }
-keepclassmembers class * implements android.location.LocationListener { public *; }
