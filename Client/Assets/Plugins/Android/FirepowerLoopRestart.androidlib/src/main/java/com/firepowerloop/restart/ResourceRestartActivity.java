package com.firepowerloop.restart;

import android.app.Activity;
import android.app.ActivityManager;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.util.Log;
import android.widget.TextView;
import java.util.List;

/** 独立进程负责一次冷启动交接；不加载 Unity、不下载资源、不修改游戏存档。 */
public final class ResourceRestartActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    /** 本次交接要结束的 Unity 主进程 PID；缺失值 -1 拒绝执行。 */
    private int mainPid;
    /** 每隔 50 毫秒检查一次，最多 40 次；只在本次 Activity 交接期间有效。 */
    private int attempts;
    /** 来自主进程的本地化失败提示；缺失时以包名作为兜底。 */
    private String failureText;

    /** 中转首次创建时校验入口，再结束旧进程；校验失败不杀进程。 */
    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        failureText = getIntent().getStringExtra("failure_text");
        mainPid = getIntent().getIntExtra("main_pid", -1);
        // 所有入口先检查，防止无效请求杀掉游戏或中转进程自己。
        if (mainPid <= 0 || mainPid == Process.myPid() || launchIntent() == null) {
            showFailure();
            return;
        }
        try {
            Process.killProcess(mainPid);
            waitForOldProcess();
        } catch (RuntimeException exception) {
            Log.e("FirepowerLoopRestart", "Restart handoff failed", exception);
            showFailure();
        }
    }

    /** 取得当前安装包的冷启动入口；无启动入口时返回 null。 */
    private Intent launchIntent() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (intent != null) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        return intent;
    }

    /** 等旧进程退出后再拉起，避免启动意图被仍存活的旧 Unity 实例接收。 */
    private void waitForOldProcess() {
        ActivityManager manager = (ActivityManager) getSystemService(ACTIVITY_SERVICE);
        List<ActivityManager.RunningAppProcessInfo> processes = manager.getRunningAppProcesses();
        if (processes != null) {
            for (ActivityManager.RunningAppProcessInfo process : processes) {
                if (process.pid == mainPid) {
                    if (++attempts >= 40) { showFailure(); return; }
                    handler.postDelayed(this::waitForOldProcess, 50);
                    return;
                }
            }
        }
        try {
            Intent intent = launchIntent();
            if (intent == null) { showFailure(); return; }
            startActivity(intent);
            finish();
            Process.killProcess(Process.myPid());
        } catch (RuntimeException exception) {
            Log.e("FirepowerLoopRestart", "Restart launch failed", exception);
            showFailure();
        }
    }

    /** 主进程已结束时保留可读提示，玩家可返回桌面手动重新打开。 */
    private void showFailure() {
        TextView text = new TextView(this);
        text.setText(failureText == null ? getPackageName() : failureText);
        text.setTextSize(20);
        text.setGravity(android.view.Gravity.CENTER);
        setContentView(text);
    }

    /** 中转销毁时撤销尚未执行的轮询，避免失效 Activity 再次拉起游戏。 */
    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }
}
