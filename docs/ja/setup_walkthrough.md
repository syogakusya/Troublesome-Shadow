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
     --preview \
     --mode shadow
   ```
   - `--preview` を付けると MediaPipe ランドマークと座席領域を重畳したプレビューウィンドウが開きます。
   - `--mode` で Unity 側に通知する動作モードを切り替えられます。`shadow` は影インスタレーション、`avatar` は Humanoid アバターのリアルタイム追従・録画モードを示します。【F:pose_capture/pose_capture_app.py†L26-L120】
   - 複数カメラが接続されている場合は `--camera` のインデックスを切り替えて確認します。
   - CLI 引数を毎回入力するのが面倒な場合は `python -m pose_capture.gui.launcher` を起動し、GUI で同じ項目を指定して「開始」をクリックすることもできます。GUI 右下の「座席を編集」ボタンを押すと座席エディタが別ウィンドウで開き、配置を変更すると即座に PoseCaptureApp へ反映されます。【F:pose_capture/gui/launcher.py†L1-L260】【F:pose_capture/gui/seating_editor.py†L1-L420】

## 3. 座席レイアウトの作成

1. GUI ランチャーから「座席を編集」を押すか、`python -m pose_capture.gui.seating_editor` を単体で実行し、背景画像（もしくは「カメラから取得」ボタンでキャプチャしたフレーム）上で椅子をドラッグして座席を登録します。保存すると正規化座標を含む JSON が生成され、ランチャーから開いた場合は変更がそのまま Python ランタイムへ送信されます。【F:pose_capture/gui/launcher.py†L120-L240】【F:pose_capture/gui/seating_editor.py†L1-L420】
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
   - `Meta.seating` に占有状況が含まれていることを確認するには、ターミナルに表示されるログまたは `--debug` オプションを利用します。【F:pose_capture/pose_capture_app.py†L26-L110】【F:pose_capture/seating.py†L32-L117】

### 座席矩形と着席判定の仕組み

- 各座席はカメラ画像に対する正規化座標（0.0〜1.0）で矩形が定義され、`SeatRegion.contains` が MediaPipe の骨格の腰中心が矩形内に収まっているかを確認します。【F:pose_capture/seating.py†L12-L61】
- 着席中と判定された場合は `SeatingLayout.evaluate` が `Meta.seating.activeSeatId` と座席ごとの境界情報を生成し、外れた場合は `Meta.seating` が削除されます。座席矩形に「ランドマークがいくつ入ったら」といった閾値はなく、腰中心1点が収まっているかどうかで決まります。【F:pose_capture/pose_capture_app.py†L57-L110】【F:pose_capture/seating.py†L62-L115】
- 信頼度は矩形の中心からどれだけ余裕があるかで決まり、矩形の半分の幅・高さを基準に余白を比率化した値（0.0〜1.0）が計算されます。矩形のサイズを広げると座っているとみなされる許容範囲が広がり、狭めると厳しくなります。【F:pose_capture/seating.py†L117-L147】

## 4. Unity プロジェクトの準備

1. Unity Hub から **6000.2.8f1** をインストールし、`Troublesome-Shadow-Unity` ディレクトリを開きます。
2. サンプルシーン（例: `Assets/Scenes/ShadowDemo.unity`。無い場合は新規シーンを作成）に以下のコンポーネントを配置します。
   - `PoseReceiver`: `ws://127.0.0.1:9000/pose` など、Python 側のエンドポイントを設定。
   - `AvatarController`: `PoseReceiver` を参照に設定し、`SampleProcessed` イベントを受け取れるようにします。
   - `InteractionModeCoordinator`: `Mode Source` を `Metadata` に設定すると Python から送られる `Meta.mode` に応じて `HumanoidPoseApplier` や `ShadowSeatDirector`、録画系コンポーネントを自動で切り替えます。`Manual` にすればシーン側で固定化も可能です。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/InteractionModeCoordinator.cs†L1-L220】
   - `HumanoidPoseApplier`（リアルタイム追従・録画用）および `ShadowSeatDirector`（影インスタレーション用）を必要に応じて追加します。`InteractionModeCoordinator` の `Avatar Only Components` / `Shadow Only Components` に登録しておくとモード切り替えに合わせて有効化されます。
   - `ShadowTouchResponder`: MediaPipe の手ランドマークが影のルート（`Shadow Root`）に近づいた際に Animator トリガー（デフォルト: `Touched`）を送出します。`Touch Radius`・`Cooldown Seconds` を現場スケールに合わせて調整してください。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowTouchResponder.cs†L1-L152】
   - `PoseLandmarkVisualizer`: シーンビューやプレイ中に MediaPipe ランドマークをギズモ表示するデバッグ用コンポーネントです。`Draw In Play Mode` と `Draw When Selected Only` を切り替えることで描画タイミングを制御できます。`Show Seating Info` を有効にすると、Python 側で検出した着席判定（席IDと信頼度）がヒップ位置付近にオーバーレイされ、しきい値調整やキャリブレーションの確認に便利です。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/PoseLandmarkVisualizer.cs†L18-L170】
3. 影キャラクター用に `ShadowSeatDirector` を設定する場合の手順:
   1. 椅子ごとに空の GameObject（アンカー）を配置し、`ShadowSeatDirector.Seats` 配列にドラッグ＆ドロップします。
   2. `_lookTarget` には観客側を向かせたい Transform、`_heightOffset` には投影面からの距離を入力します。
   3. 床座り用のアンカーを作成し、`_floorAnchor` / `_floorLookTarget` に設定します。
   4. Animator に `SeatIndex`（int）、`OnFloor`（bool）、`Scoot` / `Surprised` / `Glare` / `Frustrated` / `Sit` などのトリガーを追加し、`ShadowSeatDirector` のフィールドと一致させます。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L43-L210】
4. Humanoid アバターを録画したい場合は `HumanoidPoseApplier` と `AnimationClipRecording/HumanoidAnimationClipRecorder` を `InteractionModeCoordinator` の `Avatar Only Components` に登録し、モードが `avatar` の時のみ有効化されるようにします。収録したアニメーションクリップは `Assets/Recordings/AnimationClips` に保存されます。【F:Troublesome-Shadow-Unity/Assets/Scripts/AnimationClipRecording/Recording/HumanoidAnimationClipRecorder.cs†L1-L200】
5. 再生し、Unity コンソールに「Connected」「Frame received」などのログが表示されるか確認します。`PoseReceiver` の `Diagnostics` を有効にすると遅延や受信フレーム数を確認できます。

## 5. 投影とキャリブレーション

1. プロジェクターとスクリーン（または壁）を用意し、実際の椅子配置と Unity 内の座標が一致するように調整します。
2. 椅子・床にマーカー（ArUco など）を貼り、カメラ画像上で位置合わせができるようにしておくと再設営時に楽です。
3. Unity 側で投影面の Plane と椅子アンカーの Transform を実際の位置に合わせ、`ShadowSeatDirector` の `_movementDuration` や `_glareConfidenceThreshold` などを現場に応じて微調整します。

## 6. 動作確認チェックリスト

- [ ] Python 側で MediaPipe ランドマークが安定して取得できる。
- [ ] `Meta.seating.activeSeatId` が人の移動に応じて切り替わる。
- [ ] `Meta.mode` が `shadow` / `avatar` に切り替わり、`InteractionModeCoordinator` が対応するコンポーネントを有効化している。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/InteractionModeCoordinator.cs†L116-L190】
- [ ] Unity 側で `PoseReceiver` が接続し、影キャラクターが座席に移動する。
- [ ] 同席・隣席・満席・退席時の挙動が想定通りに再生される。【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/SeatingMetadata.cs†L15-L103】【F:Troublesome-Shadow-Unity/Assets/Scripts/Processing/ShadowSeatDirector.cs†L90-L210】
- [ ] `ShadowTouchResponder` のトリガーが期待通りに発火し、Animator 側で専用アニメーションが再生される。
- [ ] `PoseLandmarkVisualizer` のギズモ表示で MediaPipe ランドマークと椅子位置の整合が確認できる。
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
