#!/bin/bash
echo "Checking for processes using port 5000..."

# より多くの環境で動作する堅牢なポート終了処理
kill_port_robust() {
    local port=$1
    echo "Attempting to free port $port using multiple methods..."
    
    # 方法1: lsofコマンドがある場合（標準的な方法）
    if command -v lsof &> /dev/null; then
        echo "Using lsof to find processes..."
        pids=$(lsof -i :$port -t 2>/dev/null) || true
        if [ -n "$pids" ]; then
            echo "Found processes using lsof: $pids"
            for pid in $pids; do
                echo "Killing process $pid"
                kill -9 $pid 2>/dev/null || true
            done
            return 0
        fi
    fi
    
    # 方法2: netstatコマンドがある場合（代替方法）
    if command -v netstat &> /dev/null; then
        echo "Using netstat to find processes..."
        # ポートを使用しているプロセスを検索し、PIDを抽出
        pids=$(netstat -nltp 2>/dev/null | grep ":$port " | awk '{print $7}' | cut -d'/' -f1 | grep -v - | sort -u) || true
        if [ -n "$pids" ]; then
            echo "Found processes using netstat: $pids"
            for pid in $pids; do
                echo "Killing process $pid"
                kill -9 $pid 2>/dev/null || true
            done
            return 0
        fi
    fi
    
    # 方法3: ssコマンドがある場合（現代的な代替方法）
    if command -v ss &> /dev/null; then
        echo "Using ss to find processes..."
        pids=$(ss -ltnp 2>/dev/null | grep ":$port " | grep -o 'pid=[0-9]*' | cut -d= -f2) || true
        if [ -n "$pids" ]; then
            echo "Found processes using ss: $pids"
            for pid in $pids; do
                echo "Killing process $pid"
                kill -9 $pid 2>/dev/null || true
            done
            return 0
        fi
    fi
    
    # 方法4: 最終手段（fuser）
    if command -v fuser &> /dev/null; then
        echo "Using fuser as last resort..."
        fuser -k $port/tcp 2>/dev/null || true
        return 0
    fi
    
    echo "No processes found using port $port or no tools available to check"
    return 1
}

# ポート5000を解放する
kill_port_robust 5000

# 実際にポートが解放されたか短時間待機
echo "Waiting briefly to ensure port is released..."
sleep 1
echo "Port 5000 should now be available"
