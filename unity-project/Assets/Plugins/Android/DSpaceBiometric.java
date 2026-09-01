// DSpaceBiometric.java - Android biometric prompt bridge for the D-Space Unity app
//
// Unity compiles .java files under Assets/Plugins/Android into the APK, so this
// class ships inside D-Space itself. It uses the platform
// android.hardware.biometrics.BiometricPrompt (API 28+) rather than the androidx
// library because UnityPlayerActivity is not a FragmentActivity and the Unity
// build does not pull in androidx.biometric.
//
// The C# side (BiometricAuth.cs) calls authenticate() and then polls getState()
// from the Unity main thread, so Unity never blocks on the prompt.

package com.daemon.vision.dspace;

import android.app.Activity;
import android.content.Context;
import android.content.DialogInterface;
import android.content.pm.PackageManager;
import android.hardware.biometrics.BiometricManager;
import android.hardware.biometrics.BiometricPrompt;
import android.os.Build;
import android.os.CancellationSignal;
import android.util.Log;

public final class DSpaceBiometric {

    private static final String TAG = "DSpaceBiometric";

    public static final int STATE_IDLE = 0;
    public static final int STATE_IN_PROGRESS = 1;
    public static final int STATE_SUCCESS = 2;
    public static final int STATE_FAILED = 3;

    private static volatile int state = STATE_IDLE;
    private static volatile String lastError = "";
    private static volatile CancellationSignal activeSignal;

    private DSpaceBiometric() {}

    /** True when the device can show a biometric or device-credential prompt. */
    public static boolean isAvailable(Context context) {
        if (context == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.P) {
            return false;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            BiometricManager manager = context.getSystemService(BiometricManager.class);
            if (manager == null) {
                return false;
            }
            int result;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                result = manager.canAuthenticate(
                        BiometricManager.Authenticators.BIOMETRIC_WEAK
                                | BiometricManager.Authenticators.DEVICE_CREDENTIAL);
            } else {
                result = manager.canAuthenticate();
            }
            return result == BiometricManager.BIOMETRIC_SUCCESS;
        }

        // API 28: no BiometricManager; assume fingerprint hardware means a prompt can show.
        return context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_FINGERPRINT);
    }

    public static int getState() {
        return state;
    }

    public static String getLastError() {
        return lastError;
    }

    public static void reset() {
        CancellationSignal signal = activeSignal;
        if (signal != null && !signal.isCanceled()) {
            signal.cancel();
        }
        activeSignal = null;
        state = STATE_IDLE;
        lastError = "";
    }

    /** Show the prompt. Returns immediately; poll getState() for the outcome. */
    public static void authenticate(final Activity activity, final String title, final String subtitle) {
        if (activity == null) {
            fail("No activity");
            return;
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) {
            fail("BiometricPrompt requires Android 9 or newer");
            return;
        }
        if (state == STATE_IN_PROGRESS) {
            Log.w(TAG, "authenticate() called while a prompt is already showing");
            return;
        }

        state = STATE_IN_PROGRESS;
        lastError = "";

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    BiometricPrompt.Builder builder = new BiometricPrompt.Builder(activity)
                            .setTitle(title == null ? "D-Space Authentication" : title)
                            .setSubtitle(subtitle == null ? "" : subtitle);

                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                        builder.setAllowedAuthenticators(
                                BiometricManager.Authenticators.BIOMETRIC_WEAK
                                        | BiometricManager.Authenticators.DEVICE_CREDENTIAL);
                    } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        builder.setDeviceCredentialAllowed(true);
                    } else {
                        // API 28 requires a negative button when device credentials are not allowed.
                        builder.setNegativeButton("Cancel", activity.getMainExecutor(),
                                new DialogInterface.OnClickListener() {
                                    @Override
                                    public void onClick(DialogInterface dialog, int which) {
                                        fail("Cancelled by user");
                                    }
                                });
                    }

                    CancellationSignal signal = new CancellationSignal();
                    activeSignal = signal;

                    BiometricPrompt prompt = builder.build();
                    prompt.authenticate(signal, activity.getMainExecutor(),
                            new BiometricPrompt.AuthenticationCallback() {
                                @Override
                                public void onAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) {
                                    Log.i(TAG, "Biometric authentication succeeded");
                                    activeSignal = null;
                                    state = STATE_SUCCESS;
                                }

                                @Override
                                public void onAuthenticationError(int errorCode, CharSequence errString) {
                                    Log.w(TAG, "Biometric error " + errorCode + ": " + errString);
                                    activeSignal = null;
                                    fail(errString == null ? ("error " + errorCode) : errString.toString());
                                }

                                @Override
                                public void onAuthenticationFailed() {
                                    // A single bad read; the prompt stays up so the user can retry.
                                    Log.w(TAG, "Biometric read not recognised, retry allowed");
                                }
                            });
                } catch (Exception e) {
                    Log.e(TAG, "Failed to show BiometricPrompt", e);
                    fail(e.getMessage());
                }
            }
        });
    }

    private static void fail(String reason) {
        lastError = reason == null ? "" : reason;
        state = STATE_FAILED;
    }
}
