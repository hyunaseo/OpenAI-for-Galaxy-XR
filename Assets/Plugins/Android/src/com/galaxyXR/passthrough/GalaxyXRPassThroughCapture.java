package com.galaxyXR.passthrough;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.CameraMetadata;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.util.Log;

import androidx.core.app.ActivityCompat;

import java.nio.ByteBuffer;
import java.util.Arrays;

public final class GalaxyXRPassThroughCapture {

    private static final String TAG = "[GalaxyXRPassThroughCapture]";
    private static final int PERMISSION_REQUEST_CODE = 1234;

    private static CameraDevice cameraDevice;
    private static CameraCaptureSession captureSession;
    private static ImageReader imageReader;
    private static String cameraId;
    private static Activity activity;
    private static byte[] latestJpeg;

    private GalaxyXRPassThroughCapture() {
    }

    public static void start(Activity hostActivity, int width, int height) {
        activity = hostActivity;

        if (!hasCameraPermission()) {
            requestCameraPermission();
            Log.w(TAG, "Camera permission not granted.");
            return;
        }

        try {
            CameraManager manager = (CameraManager) activity.getSystemService(Context.CAMERA_SERVICE);
            Handler mainHandler = new Handler(activity.getMainLooper());

            logAvailableCameras(manager);
            cameraId = selectBackCamera(manager);

            if (cameraId == null) {
                Log.e(TAG, "No back camera found.");
                return;
            }

            imageReader = ImageReader.newInstance(width, height, android.graphics.ImageFormat.JPEG, 2);
            imageReader.setOnImageAvailableListener(reader -> {
                Image image = reader.acquireLatestImage();
                if (image == null) {
                    return;
                }

                try {
                    Image.Plane[] planes = image.getPlanes();
                    if (planes != null && planes.length > 0) {
                        ByteBuffer buffer = planes[0].getBuffer();
                        byte[] jpeg = new byte[buffer.remaining()];
                        buffer.get(jpeg);
                        latestJpeg = jpeg;
                        Log.d(TAG, "Updated latest JPEG, size=" + latestJpeg.length);
                    }
                } catch (Exception e) {
                    Log.e(TAG, "Failed to read JPEG image.", e);
                } finally {
                    image.close();
                }
            }, mainHandler);

            manager.openCamera(cameraId, new CameraDevice.StateCallback() {
                @Override
                public void onOpened(CameraDevice camera) {
                    cameraDevice = camera;
                    startPreview();
                }

                @Override
                public void onDisconnected(CameraDevice camera) {
                    camera.close();
                    Log.w(TAG, "Camera disconnected.");
                }

                @Override
                public void onError(CameraDevice camera, int error) {
                    camera.close();
                    Log.e(TAG, "Camera error: " + error);
                }
            }, mainHandler);

        } catch (Exception e) {
            Log.e(TAG, "Failed to start camera.", e);
        }
    }

    public static void stop() {
        try {
            if (captureSession != null) {
                captureSession.stopRepeating();
                captureSession.close();
                captureSession = null;
            }
            if (cameraDevice != null) {
                cameraDevice.close();
                cameraDevice = null;
            }
            if (imageReader != null) {
                imageReader.close();
                imageReader = null;
            }
            latestJpeg = null;
        } catch (Exception e) {
            Log.e(TAG, "Failed to stop camera.", e);
        }
    }

    public static byte[] getLatestJpeg() {
        return latestJpeg;
    }

    private static boolean hasCameraPermission() {
        return ActivityCompat.checkSelfPermission(activity, Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED;
    }

    private static void requestCameraPermission() {
        ActivityCompat.requestPermissions(
                activity,
                new String[]{Manifest.permission.CAMERA},
                PERMISSION_REQUEST_CODE
        );
    }

    private static void logAvailableCameras(CameraManager manager) throws CameraAccessException {
        String[] cameraIds = manager.getCameraIdList();
        Log.i(TAG, "Detected cameras: " + Arrays.toString(cameraIds));

        for (String id : cameraIds) {
            try {
                CameraCharacteristics characteristics = manager.getCameraCharacteristics(id);
                Integer facing = characteristics.get(CameraCharacteristics.LENS_FACING);
                Integer level = characteristics.get(CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL);

                String facingLabel = facing == null ? "UNKNOWN" :
                        facing == CameraCharacteristics.LENS_FACING_BACK ? "BACK" :
                        facing == CameraCharacteristics.LENS_FACING_FRONT ? "FRONT" :
                        facing == CameraCharacteristics.LENS_FACING_EXTERNAL ? "EXTERNAL" : "UNKNOWN";

                String levelLabel = level == null ? "UNKNOWN" :
                        level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LEGACY ? "LEGACY" :
                        level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LIMITED ? "LIMITED" :
                        level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_FULL ? "FULL" :
                        level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_3 ? "LEVEL_3" :
                        level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_EXTERNAL ? "EXTERNAL" : "UNKNOWN";

                Log.i(TAG, "Camera ID: " + id + " | Facing: " + facingLabel + " | Level: " + levelLabel);
            } catch (Exception e) {
                Log.e(TAG, "Failed to read camera characteristics for " + id, e);
            }
        }
    }

    private static String selectBackCamera(CameraManager manager) throws CameraAccessException {
        for (String id : manager.getCameraIdList()) {
            CameraCharacteristics characteristics = manager.getCameraCharacteristics(id);
            Integer facing = characteristics.get(CameraCharacteristics.LENS_FACING);
            if (facing != null && facing == CameraCharacteristics.LENS_FACING_BACK) {
                Log.i(TAG, "Selected cameraId=" + id);
                return id;
            }
        }
        return null;
    }

    private static void startPreview() {
        if (cameraDevice == null || imageReader == null) {
            Log.e(TAG, "Cannot start preview: camera or image reader unavailable.");
            return;
        }

        try {
            CaptureRequest.Builder builder =
                    cameraDevice.createCaptureRequest(CameraDevice.TEMPLATE_RECORD);
            builder.addTarget(imageReader.getSurface());
            builder.set(CaptureRequest.CONTROL_MODE, CameraMetadata.CONTROL_MODE_AUTO);

            cameraDevice.createCaptureSession(
                    Arrays.asList(imageReader.getSurface()),
                    new CameraCaptureSession.StateCallback() {
                        @Override
                        public void onConfigured(CameraCaptureSession session) {
                            captureSession = session;
                            try {
                                captureSession.setRepeatingRequest(builder.build(), null, null);
                                Log.i(TAG, "Capture session configured.");
                            } catch (CameraAccessException e) {
                                Log.e(TAG, "Failed to start repeating request.", e);
                            }
                        }

                        @Override
                        public void onConfigureFailed(CameraCaptureSession session) {
                            Log.e(TAG, "Failed to configure capture session.");
                        }
                    },
                    null
            );
        } catch (CameraAccessException e) {
            Log.e(TAG, "Failed to start preview.", e);
        }
    }
}