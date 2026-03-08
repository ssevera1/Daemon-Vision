package com.daemon.vision.companion;

import android.app.Activity;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.biometric.BiometricPrompt;
import androidx.core.content.ContextCompat;
import androidx.fragment.app.FragmentActivity;

import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * BiometricBridge — Called from Unity to trigger Android biometric authentication.
 * In the Daemon, HUD glasses are biometrically keyed. This bridges Android's
 * BiometricPrompt to Unity's authentication flow.
 */
public class BiometricBridge {

    private static final String TAG = "DaemonBiometric";
    private static final AtomicBoolean authResult = new AtomicBoolean(false);
    private static volatile boolean authComplete = false;

    /**
     * Trigger biometric authentication. Called from Unity via JNI.
     * Returns true if authentication succeeds.
     */
    public static boolean authenticate(Activity activity) {
        if (!(activity instanceof FragmentActivity)) {
            Log.e(TAG, "Activity must be FragmentActivity for BiometricPrompt");
            return false;
        }

        authResult.set(false);
        authComplete = false;

        FragmentActivity fragmentActivity = (FragmentActivity) activity;
        Executor executor = ContextCompat.getMainExecutor(activity);

        BiometricPrompt.PromptInfo promptInfo = new BiometricPrompt.PromptInfo.Builder()
                .setTitle("D-Space Authentication")
                .setSubtitle("Verify identity to access the darknet")
                .setDescription("Biometric verification required for D-Space access. " +
                        "Your identity is stored locally and never transmitted.")
                .setNegativeButtonText("Cancel")
                .setAllowedAuthenticators(
                        android.hardware.biometrics.BiometricManager.Authenticators.BIOMETRIC_STRONG |
                        android.hardware.biometrics.BiometricManager.Authenticators.DEVICE_CREDENTIAL)
                .build();

        activity.runOnUiThread(() -> {
            BiometricPrompt biometricPrompt = new BiometricPrompt(fragmentActivity,
                    executor, new BiometricPrompt.AuthenticationCallback() {

                @Override
                public void onAuthenticationSucceeded(@NonNull BiometricPrompt.AuthenticationResult result) {
                    Log.i(TAG, "Biometric authentication succeeded");
                    authResult.set(true);
                    authComplete = true;
                }

                @Override
                public void onAuthenticationError(int errorCode, @NonNull CharSequence errString) {
                    Log.w(TAG, "Biometric auth error: " + errString);
                    authResult.set(false);
                    authComplete = true;
                }

                @Override
                public void onAuthenticationFailed() {
                    Log.w(TAG, "Biometric authentication failed");
                    // Don't set complete — BiometricPrompt may retry
                }
            });

            biometricPrompt.authenticate(promptInfo);
        });

        // Wait for result (with timeout)
        long startTime = System.currentTimeMillis();
        while (!authComplete && System.currentTimeMillis() - startTime < 30000) {
            try {
                Thread.sleep(100);
            } catch (InterruptedException e) {
                break;
            }
        }

        return authResult.get();
    }
}
