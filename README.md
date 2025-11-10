# Troublesome Shadow Pose Pipeline

このリポジトリは、Python 製の姿勢推定プログラムから Unity アバターへ人のポーズデータをストリーミングするためのリファレンス実装です。

## コンポーネント

### Python PoseCaptureApp
* `pose_capture/` 配下に配置されています。
* MediaPipe を利用して Web カメラから人体ランドマークを取得し、WebSocket または UDP で送信します。
* `pose_capture/providers.py` の `MediaPipeSkeletonProvider` がカメラの初期化と推定処理、Unity 向け 3D 座標の生成を担当します。
* `pose_capture/pose_capture_app.py` はプロバイダ・トランスポート・カメラインデックス・フレーム間隔などを設定できる CLI を提供します。

### Unity Runtime
* スクリプトは `Troublesome-Shadow-Unity/Assets/Scripts/` にあり、`Data` / `Networking` / `Processing` / `UI` に整理されています。
* `PoseReceiver` がネットワーク接続とフレームキューを管理し、`AvatarController` が正規化後のスケルトンをリスナーに中継します。
* `HumanoidPoseApplier` が Animator を直接駆動して Humanoid アバターにポーズを適用します。
* （任意）`SkeletonNormalizer` を使って MediaPipe 座標系と Unity 座標系のスケール / 向きを調整できます。
* `DiagnosticsPanel` は遅延やキューの状態などの指標を表示します。

## 利用手順の概要

1. `SkeletonProvider`（例: `MediaPipeSkeletonProvider`）とトランスポート（`WebSocketSkeletonTransport` もしくは `UDPSkeletonTransport`）を用意します。
2. `CaptureConfig` を作成し、`PoseCaptureApp` のイベントループを動かして Unity へ骨格フレームを送ります。
3. Unity シーンに各コンポーネントを追加します。
   * `PoseReceiver` を配置し、Python 側のホスト情報を設定します。
   * `AvatarController` に `PoseReceiver`（および必要なら `SkeletonNormalizer`、`PosePlayback`）を割り当てます。
   * `HumanoidPoseApplier` を同じ GameObject に追加し、`Animator` と `AvatarController` を参照に設定します。
   * 必要に応じて `DiagnosticsPanel` を追加し、ステータス表示を行います。
4. 必要に応じて再接続処理やキャリブレーションのフローを組み込みます。

### 座席インタラクションの設定

影キャラクターを椅子の占有状況に応じて動かす場合は、次のドキュメントを順に参照してください。

1. **セットアップ全体像**: `docs/ja/setup_walkthrough.md` で Python / Unity 双方の準備と動作確認を行います。
2. **座席メタデータの詳細**: `docs/ja/seating_integration_guide.md` で `--seating-config` の作成や `ShadowSeatDirector` の調整方法を確認します。

サンプルのレイアウト JSON は `docs/examples/seating_layout.example.json` に含まれています。

### GUI ツール

- 座席の矩形を視覚的に作成するには `python -m pose_capture.gui.seating_editor` を実行し、背景画像（またはカメラフレーム）上で座席をドラッグして保存します。【F:pose_capture/gui/seating_editor.py†L1-L236】
- CLI オプションを迷わずに起動したい場合は `python -m pose_capture.gui.launcher` を実行し、GUI でカメラインデックスやメタデータファイルを指定して PoseCaptureApp を開始できます。【F:pose_capture/gui/launcher.py†L1-L230】

## Python キャプチャ環境の準備

MediaPipe と OpenCV、WebSocket クライアントをインストールします。

```bash
python -m pip install mediapipe opencv-python websockets
```

Unity へ送信を開始するには次のように実行します（`--preview` を付けるとプレビューウィンドウが表示されます）。

```bash
python -m pose_capture.pose_capture_app \
  --provider mediapipe \
  --transport ws \
  --endpoint 0.0.0.0:9000/pose \
  --camera 0 \
  --frame-interval 0.016 \
  --preview
```

主な引数:
- `--camera`: 使用する Web カメラのインデックス。
- `--endpoint`: WebSocket の場合は `ws://host:port/path` 形式、もしくは `host:port/path`（デフォルトは WebSocket）。
- `--metadata` / `--calibration`: それぞれメタデータやキャリブレーション情報を含む JSON ファイルへのパス。
- `--preview`: OpenCV ウィンドウにカメラ映像と MediaPipe のランドマークを重畳表示します。ESC キーでプレビューのみ停止できます。

## Unity でのワークフロー

1. `PoseReceiver` コンポーネントをシーンに追加し、Python 側のアドレス・ポート・パスを設定します。
2. `PoseReceiver` と `AvatarController` をアバターのルートに配置し、必要に応じて `SkeletonNormalizer` を割り当てます。
   - Humanoid モデルをリアルタイムで動かす場合は同じ GameObject に `HumanoidPoseApplier` を追加し、`Animator` と `AvatarController` を参照に設定してください。
   - `HumanoidPoseApplier` の `autoPopulate` を有効にすると MediaPipe の主要ジョイントと Humanoid ボーンの既定マッピングが生成されます。必要に応じて `boneMappings` や `rotationOffset` を調整します。
3. （任意）`PoseRecorder` を `AvatarController` と同じ GameObject に追加すると、セッションを記録できます。
   - UI ボタンなどから `StartRecording()` / `StopRecording()` を呼び出すと、`Application.persistentDataPath/Recordings/` 以下に JSON が保存されます。
4. （任意）`PosePlayback` コンポーネントを追加すると、録画したモーションを再生できます。
   - `PoseRecorder` が生成した JSON を `PlayFromFile(path)` で読み込むか、`LastRecording` を `Play(recording)` に渡します。
   - `AvatarController._playback` に割り当てると、再生中はライブデータよりも録画データが優先されます。
   - Humanoid へ再生する場合も `HumanoidPoseApplier` が Animator のボーンを駆動します。

## 録画ファイルと再生

`PoseRecorder` は以下のフィールドを含む `.json` を出力します。
- `name`: セッション名。
- `durationMs`: ミリ秒単位の収録時間。
- `frames`: タイムスタンプ付きの骨格フレーム（位置・回転・信頼度を保持）。
- `meta`: Python 側で付与したメタデータ（カメラインデックスなど）。

`PosePlayback` はループ再生や再生速度変更に対応しており、Python が接続されていなくても `AvatarController` 経由でアバターを動かせます。
