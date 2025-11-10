# 座席インタラクション実装ガイド

このドキュメントでは、Python 側の PoseCaptureApp と Unity ランタイムを連携させて、観客の着席状況に応じて影キャラクターが動くインタラクションを構築する手順を説明します。

## 1. 座席レイアウトの定義

1. Web カメラ映像上で各椅子の座面を囲む矩形を決め、`xMin`, `xMax`, `yMin`, `yMax` を 0.0〜1.0 の正規化座標で記録します。
   - `0.0` は画面左（または上）、`1.0` は画面右（または下）を表します。
   - 座面より少し広めに設定すると検出が安定します。
2. `docs/examples/seating_layout.example.json` をコピーし、座席ごとに ID と境界値を調整します。
3. ファイルを Python 実行環境から参照できる場所に保存します。

```jsonc
{
  "seats": [
    { "id": "seat-1", "bounds": { "xMin": 0.08, "xMax": 0.28, "yMin": 0.55, "yMax": 0.92 } }
    // ... 他の座席を追加
  ]
}
```

## 2. Python: PoseCaptureApp の設定

1. 依存関係をインストールします。
   ```bash
   python -m pip install mediapipe opencv-python websockets
   ```
2. PoseCaptureApp を `--seating-config` オプション付きで起動します。
   ```bash
   python -m pose_capture.pose_capture_app \
     --provider mediapipe \
     --transport ws \
     --endpoint 0.0.0.0:9000/pose \
     --seating-config /path/to/your/seating.json \
     --frame-interval 0.016
   ```
3. 実行中は各フレームに `Meta.seating` フィールドが追加され、Unity から占有情報を受け取れるようになります。【F:pose_capture/pose_capture_app.py†L57-L85】【F:pose_capture/seating.py†L32-L115】

### 2.1 メタデータの内容

- `activeSeatId`: 観客が座っていると推定した椅子 ID（なければ `null`）。
- `confidence`: 座面中央からの距離に応じた 0.0〜1.0 の信頼度。
- `seats`: 各座席の ID・占有状態・正規化境界。
  ```json
  {
    "activeSeatId": "seat-2",
    "confidence": 0.63,
    "seats": [
      { "id": "seat-1", "occupied": false, "bounds": { "xMin": 0.08, ... } },
      { "id": "seat-2", "occupied": true,  "bounds": { "xMin": 0.33, ... } }
    ]
  }
  ```

## 3. Unity セットアップ

1. シーン内の影アバターに次のコンポーネントを割り当てます。
   - `PoseReceiver`: Python 側の WebSocket/UDP エンドポイントを設定。
   - `AvatarController`: `PoseReceiver` を参照に設定し、`SampleProcessed` イベントが発火するようにします。
   - `ShadowSeatDirector`: 影キャラクターの移動とアニメーションを制御します。
2. `ShadowSeatDirector` の `Seats` 配列に椅子ごとのアンカーを設定します。
   - 各 `ShadowSeat` の `_anchor` には椅子座面の Transform、`_lookTarget` には視線を向けたい対象の Transform を指定します。
   - `_heightOffset` で投影の高さ調整が可能です。
3. `Floor Anchor` / `Floor Look Target` に床に座り込む際の位置・向きを指定します。
4. Animator に次のパラメータを用意し、`ShadowSeatDirector` のフィールドと一致させます。
   - `SeatIndex` (int)、`OnFloor` (bool)、`Scoot` / `Surprised` / `Glare` / `Frustrated` / `Sit` などのトリガー。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L43-L210】
5. 再生中に `AvatarController` が受け取ったサンプルから `SeatingSnapshot` が抽出され、影の挙動が更新されます。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/SeatingMetadata.cs†L15-L103】【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L90-L210】

## 4. 動作確認

1. Python 側を起動し、Unity シーンを再生します。
2. カメラの映像内で椅子に着席・接近すると、`ShadowSeatDirector` が以下の挙動を行います。
   - 同じ椅子に座ると驚いて別の席へ移動（空席が無ければ床へ）。
   - 隣に座ると一席空けるように移動。
   - 全席が埋まると床に座り込む。
   - 一定間隔で迷惑そうに睨むアニメーション（信頼度が閾値以上の場合）。
3. Unity コンソールまたは Animator パラメータを確認し、意図したステート遷移になっているか検証します。

## 5. 運用ヒント

- 座席境界は現場で微調整し、観客の身長差に応じて `SeatingLayout` を更新してください。
- WebSocket の遅延が気になる場合は UDP 転送に切り替えることも可能です（`--transport udp --endpoint host:port`）。
- `ShadowSeatDirector._glareConfidenceThreshold` や `_movementDuration` を調整して、会場の雰囲気に合わせた演出を作り込めます。

