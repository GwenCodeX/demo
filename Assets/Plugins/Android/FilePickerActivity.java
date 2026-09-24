package com.gwencodex.rhythmplayer;

import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.util.Log;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

public class FilePickerActivity extends UnityPlayerActivity
{
    private static final int REQUEST_CODE = 9911;

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_CODE)
        {
            return;
        }

        if (resultCode != RESULT_OK || data == null || data.getData() == null)
        {
            UnityPlayer.UnitySendMessage("GameRoot", "OnFilePicked", "");
            return;
        }

        Uri uri = data.getData();
        String displayName = queryDisplayName(uri);

        try
        {
            File targetDir = new File(getExternalFilesDir(null), "Songs");
            if (!targetDir.exists())
            {
                targetDir.mkdirs();
            }

            File targetFile = new File(targetDir, displayName);
            InputStream input = getContentResolver().openInputStream(uri);
            FileOutputStream output = new FileOutputStream(targetFile);
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) > 0)
            {
                output.write(buffer, 0, read);
            }
            output.flush();
            output.close();
            input.close();

            UnityPlayer.UnitySendMessage("GameRoot", "OnFilePicked", targetFile.getAbsolutePath());
        }
        catch (Exception e)
        {
            Log.e("RhythmPlayer", "import failed", e);
            UnityPlayer.UnitySendMessage("GameRoot", "OnFilePicked", "ERROR:" + e.getMessage());
        }
    }

    private String queryDisplayName(Uri uri)
    {
        try
        {
            Cursor cursor = getContentResolver().query(uri, null, null, null, null);
            if (cursor != null)
            {
                try
                {
                    if (cursor.moveToFirst())
                    {
                        int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                        if (index >= 0)
                        {
                            return cursor.getString(index);
                        }
                    }
                }
                finally
                {
                    cursor.close();
                }
            }
        }
        catch (Exception ignored)
        {
        }

        String last = uri.getLastPathSegment();
        return last != null ? last : "imported_song";
    }
}
