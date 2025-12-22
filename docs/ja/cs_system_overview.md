## ランタイム全体像
- PoseReceiver が WebSocket/UDP でスケルトン JSON を受信し、キューに積む。
- AvatarController が受信データを SkeletonNormalizer で正規化し、SampleProcessed イベントとして配信。
- InteractionModeCoordinator がサンプルのメタデータまたは手動指定でモードを判定し、Shadow/Avatar 系の挙動を切り替える。
- Shadow 系: ShadowSeatDirector が座席メタデータを読み取り、座席移動とアニメーションを制御。ShadowTouchResponder が手先との接触を検知し、アニメーションをトリガー。PoseLandmarkVisualizer がランドマークをギズモ表示。
- Avatar 系: HumanoidPoseApplier が Animator へリターゲットし、メタデータによるルート位置補正や ShadowSeatDirector の移動中抑制を行う。
- 録画/再生: HumanoidAnimationClipRecorder が Animator を記録し、AnimationClipConverter で JSON 変換。HumanoidAnimationClipPlayer が AnimationClip または JSON を再生。

## スクリプトと役割
- `Assets/Scripts/Data/SkeletonData.cs`：JointSample・SkeletonSample とメタデータ保持。全処理の基本データ型。
- `Assets/Scripts/Networking/PoseReceiver.cs`：WebSocket/UDP 受信とバッファ。Connected/Disconnected イベントを発火。
- `Assets/Scripts/Processing/AvatarController.cs`：受信サンプルの正規化と配信。SkeletonNormalizer を適用。
- `Assets/Scripts/Processing/SkeletonNormalizer.cs`：スケール・オフセット・軸反転やジョイント別補正を定義する ScriptableObject。`Assets/SkeletonNormalizer.asset` で設定。
- `Assets/Scripts/Processing/InteractionModeCoordinator.cs`：ShadowInstallation / HumanoidAvatar モード切替。ShadowSeatDirector, HumanoidPoseApplier, Recorder/Player などの有効・無効をまとめて制御。
- `Assets/Scripts/Processing/ShadowSeatDirector.cs`：座席メタデータを解析し、空席選択・移動・アニメーション管理。モード切替と移動状態の公開を行う。
- `Assets/Scripts/Processing/HumanoidPoseApplier.cs`：MediaPipe 系スケルトンを Animator に適用。メタデータからルート移動を算出し、ClipPlayer 再生時や ShadowSeatDirector の移動中は適用をスキップ。
- `Assets/Scripts/Processing/ShadowTouchResponder.cs`：手のランドマークが影の近くに来たとき指定トリガーを発火。
- `Assets/Scripts/Processing/PoseLandmarkVisualizer.cs`：ランドマークと接続線をギズモ描画。座席メタ情報の簡易表示も担当。
- `Assets/Scripts/Processing/SeatingMetadata.cs`：座席メタデータのパースユーティリティ（SeatingSnapshot）。
- `Assets/Scripts/AnimationClipRecording/Recording/HumanoidAnimationClipRecorder.cs`：Animator の階層／Humanoid Muscles を収録し、AnimationClip と JSON を生成。
- `Assets/Scripts/AnimationClipRecording/Playback/HumanoidAnimationClipPlayer.cs`：AnimationClip または JSON を読み込み再生し、元ポーズをキャッシュして復元。
- `Assets/Scripts/AnimationClipRecording/Utils/AnimationClipConverter.cs`：AnimationClip と JSON の相互変換。

## 主なアセット/プレハブでの適用
- `Assets/Kevin Iglesias/Human Character Dummy/Prefabs/HumanDummy_M White.prefab`：InteractionModeCoordinator・PoseReceiver・AvatarController・HumanoidPoseApplier・ShadowSeatDirector・ShadowTouchResponder・PoseLandmarkVisualizer・Recorder/Player をバンドル。
- `Assets/vtuber.unity`：HumanoidAnimationClipPlayer / Recorder を配置。
- `Assets/SkeletonNormalizer.asset`：受信スケルトン正規化の設定保持。

## 今回削除した未使用スクリプト
- `Assets/Scripts/UI/DiagnosticsPanel.cs`（未参照の UI デバッグパネル）

