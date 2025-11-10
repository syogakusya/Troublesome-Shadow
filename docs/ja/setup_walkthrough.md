# セットアップ手順（Python & Unity）

このドキュメントでは、Troublesome Shadow プロジェクトをゼロから構築し、Python 側の PoseCaptureApp と Unity 側のシーンを接続して動作確認するまでの具体的な手順を説明します。

## 0. 前提条件

| 区分 | 推奨環境 |
| --- | --- |
| OS | Windows 10/11 または macOS 13+ |
| Python | 3.10 以上（64bit） |
| Unity | **6000.2.8f1**（`Troublesome-Shadow-Unity/ProjectSettings/ProjectVersion.txt` 参照） |
| ハードウェア | Web カメラ、プロジェクター、GPU 搭載 PC（MediaPipe をリアルタイム推定できる程度） |
| その他 | プロジェクターと椅子の位置合わせができる設営スペース |

> **Tip:** macOS の場合は Python 仮想環境の作成に `python3 -m venv` を、Windows の場合は PowerShell で `py -3 -m venv` を使用してください。

## 1. リポジトリの取得

```bash
# 任意の作業ディレクトリで実行
git clone https://github.com/your-org/Troublesome-Shadow.git
cd Troublesome-Shadow
```

## 2. Python 実行環境のセットアップ

1. 仮想環境を作成・有効化します。
   ```bash
   python3 -m venv .venv
   source .venv/bin/activate   # Windows の場合: .\.venv\Scripts\Activate.ps1
   ```
2. 依存関係をインストールします。
   ```bash
   pip install --upgrade pip
   pip install -r requirements.txt  # ファイルが無い場合は下記を直接インストール
   pip install mediapipe opencv-python websockets numpy
   ```
3. Web カメラの映像を確認しつつ PoseCaptureApp を起動します。
   ```bash
   python -m pose_capture.pose_capture_app \
     --provider mediapipe \
     --transport ws \
     --endpoint 0.0.0.0:9000/pose \
     --camera 0 \
     --frame-interval 0.016 \
     --preview
   ```
   - `--preview` を付けると MediaPipe ランドマークと座席領域を重畳したプレビューウィンドウが開きます。
   - 複数カメラが接続されている場合は `--camera` のインデックスを切り替えて確認します。
   - CLI 引数を毎回入力するのが面倒な場合は `python -m pose_capture.gui.launcher` を起動し、GUI で同じ項目を指定して「開始」をクリックすることもできます。【F:pose_capture/gui/launcher.py†L1-L230】

## 3. 座席レイアウトの作成

1. `python -m pose_capture.gui.seating_editor` を実行し、背景画像（もしくは「カメラから取得」ボタンでキャプチャしたフレーム）上で椅子をドラッグして座席を登録します。保存すると正規化座標を含む JSON が生成されます。【F:pose_capture/gui/seating_editor.py†L1-L236】
2. 既存ファイルを直接編集する場合は `docs/examples/seating_layout.example.json` をコピーし、現場の椅子数に合わせて調整します。
   ```bash
   cp docs/examples/seating_layout.example.json seating.json
   ```
3. プレビューで椅子座面を確認しながら、`xMin`〜`yMax` を 0.0〜1.0 の正規化座標で調整します。
4. 完成した JSON を PoseCaptureApp 起動時の `--seating-config` に渡します。
   ```bash
   python -m pose_capture.pose_capture_app \
     --provider mediapipe \
     --transport ws \
     --endpoint 0.0.0.0:9000/pose \
     --camera 0 \
     --frame-interval 0.016 \
     --seating-config ./seating.json \
     --preview
   ```
   - `Meta.seating` に占有状況が含まれていることを確認するには、ターミナルに表示されるログまたは `--debug` オプション（GUI ランチャーでは「デバッグログを有効化」チェックボックス）を利用します。【F:pose_capture/pose_capture_app.py†L57-L85】【F:pose_capture/seating.py†L32-L115】

## 4. Unity プロジェクトの準備

1. Unity Hub から **6000.2.8f1** をインストールし、`Troublesome-Shadow-Unity` ディレクトリを開きます。
2. サンプルシーン（例: `Assets/Scenes/ShadowDemo.unity`。無い場合は新規シーンを作成）に以下のコンポーネントを配置します。
   - `PoseReceiver`: `ws://127.0.0.1:9000/pose` など、Python 側のエンドポイントを設定。
   - `AvatarController`: `PoseReceiver` を参照に設定し、`SampleProcessed` イベントを受け取れるようにします。
   - `HumanoidPoseApplier` または `ShadowSeatDirector` など、必要な処理コンポーネントを追加します。
3. 影キャラクター用に `ShadowSeatDirector` を設定する場合の手順:
   1. 椅子ごとに空の GameObject（アンカー）を配置し、`ShadowSeatDirector.Seats` 配列にドラッグ＆ドロップします。
   2. `_lookTarget` には観客側を向かせたい Transform、`_heightOffset` には投影面からの距離を入力します。
   3. 床座り用のアンカーを作成し、`_floorAnchor` / `_floorLookTarget` に設定します。
   4. Animator に `SeatIndex`（int）、`OnFloor`（bool）、`Scoot` / `Surprised` / `Glare` / `Frustrated` / `Sit` などのトリガーを追加し、`ShadowSeatDirector` のフィールドと一致させます。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L43-L210】
4. 再生し、Unity コンソールに「Connected」「Frame received」などのログが表示されるか確認します。`PoseReceiver` の `Diagnostics` を有効にすると遅延や受信フレーム数を確認できます。

## 5. 投影とキャリブレーション

1. プロジェクターとスクリーン（または壁）を用意し、実際の椅子配置と Unity 内の座標が一致するように調整します。
2. 椅子・床にマーカー（ArUco など）を貼り、カメラ画像上で位置合わせができるようにしておくと再設営時に楽です。
3. Unity 側で投影面の Plane と椅子アンカーの Transform を実際の位置に合わせ、`ShadowSeatDirector` の `_movementDuration` や `_glareConfidenceThreshold` などを現場に応じて微調整します。

## 6. 動作確認チェックリスト

- [ ] Python 側で MediaPipe ランドマークが安定して取得できる。
- [ ] `Meta.seating.activeSeatId` が人の移動に応じて切り替わる。
- [ ] Unity 側で `PoseReceiver` が接続し、影キャラクターが座席に移動する。
- [ ] 同席・隣席・満席・退席時の挙動が想定通りに再生される。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/SeatingMetadata.cs†L15-L103】【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L90-L210】
- [ ] プロジェクターの投影と椅子位置が視覚的に合っている。

## 7. トラブルシューティング

| 症状 | 対処 |
| --- | --- |
| Python から Unity に接続できない | ファイアウォール設定・IP アドレス・ポート番号を再確認。`--transport udp` に切り替えて遅延を減らすことも可。|
| 座席の判定が不安定 | `seating.json` の座標を広めに設定、カメラ角度を調整、照明を一定に保つ。|
| 影が椅子に座らない | Animator パラメータ名が `ShadowSeatDirector` のフィールドと一致しているか、`SeatIndex` が -1 でないか確認。|
| 床に座り込んだまま戻らない | `_floorReturnDelay` を短くするか、満席判定が解除されるよう座席の占有条件を調整。|

## 8. 追加資料

- 座席メタデータの構造: `docs/ja/seating_integration_guide.md`
- 全体構成の企画メモ: `docs/ja/interactive_shadow_plan.md`
- サンプル座席レイアウト: `docs/examples/seating_layout.example.json`

以上で、Troublesome Shadow を現場に展開するための基本的なセットアップは完了です。現地での設営時は、照明やカメラの位置による誤検出が起きないかを確認しながら数回リハーサルを行ってください。
