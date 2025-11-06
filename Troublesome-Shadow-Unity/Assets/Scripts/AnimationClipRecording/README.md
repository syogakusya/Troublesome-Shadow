# AnimationClip Recording 機能

Unity上でHumanoidアバターの動きを記録してAnimationClipとして保存・再生する機能です。既存のPoseRuntime実装から完全に分離されています。

## 機能概要

- **Humanoidアバターの動きを記録**: Humanoidアバターのボーン回転とルート位置を記録
- **AnimationClipとして保存**: Unityの標準AnimationClip形式で保存
- **JSON形式での保存**: 編集・変換が容易なJSON形式でも保存可能
- **AnimationClipの再生**: 記録したAnimationClipを再生
- **JSONからの読み込み**: JSONファイルからAnimationClipを読み込んで再生
- **UnityEditor上での設定**: カスタムInspectorで簡単に設定・操作可能

## ディレクトリ構造

```
Assets/Scripts/AnimationClipRecording/
├── Recording/
│   └── HumanoidAnimationClipRecorder.cs      # 記録コンポーネント
├── Playback/
│   └── HumanoidAnimationClipPlayer.cs        # 再生コンポーネント
├── Utils/
│   └── AnimationClipConverter.cs             # JSON変換ユーティリティ
└── Editor/
    └── HumanoidAnimationClipRecorderEditor.cs # カスタムエディタ
```

## スクリプトの関係図

```
┌─────────────────────────────────────────────────────────┐
│  HumanoidAnimationClipRecorder                          │
│  - Humanoidアバターの動きを記録                          │
│  - AnimationClipを生成                                   │
│  - JSON形式でエクスポート                                 │
└──────┬──────────────────────────────────────────────────┘
       │
       ├───> AnimationClip (Unity標準形式)
       │     └───> Assets/Recordings/AnimationClips/
       │
       └───> JSON (編集可能形式)
             └───> Application.persistentDataPath/Recordings/Json/
                    │
                    └───> AnimationClipConverter
                          └───> AnimationClip に変換可能
                                 │
                                 └───> HumanoidAnimationClipPlayer
                                       └───> PlayableGraph経由で再生
```

## UnityEditor上での設定方法

### 1. HumanoidAnimationClipRecorderの設定

1. Humanoidアバターが設定されたGameObjectを選択
2. `Add Component` → `AnimationClipRecording` → `Humanoid Animation Clip Recorder`を追加
3. Inspectorで以下の設定を行います:

   - **Animator**: Animatorコンポーネント（自動検出されます）
   - **Recording Name**: 記録するAnimationClipの名前
   - **Frame Rate**: 記録フレームレート（デフォルト: 60fps）
   - **Record Root Transform**: ルートTransformの位置・回転を記録するか
   - **Record All Human Bones**: すべてのHumanボーンを記録するか
   - **Selected Bones**: 特定のボーンのみ記録する場合に設定
   - **Save To Assets**: AnimationClipをAssetsフォルダに保存するか
   - **Assets Path**: Assetsフォルダ内の保存パス
   - **Export Json**: JSON形式でも保存するか
   - **Json Output Path**: JSONファイルの保存パス

4. プレイモードに入り、`Start Recording`ボタンをクリック
5. アバターを動かす
6. `Stop Recording`ボタンをクリックして記録を終了

### 2. HumanoidAnimationClipPlayerの設定

1. Humanoidアバターが設定されたGameObjectを選択
2. `Add Component` → `AnimationClipRecording` → `Humanoid Animation Clip Player`を追加
3. Inspectorで以下の設定を行います:

   - **Animator**: Animatorコンポーネント（自動検出されます）
   - **Animation Clip**: 再生するAnimationClip
   - **Play On Start**: 開始時に自動再生するか
   - **Loop**: ループ再生するか
   - **Speed**: 再生速度（1.0が通常速度）

4. プレイモードに入り、`Play`ボタンをクリックして再生

### 3. JSONからの読み込み

`HumanoidAnimationClipPlayer`のInspectorで:
- `JSON Path`フィールドにJSONファイルのパスを入力
- `Load and Play`ボタンをクリックして読み込み・再生

## 使用例

### スクリプトからの使用

```csharp
using AnimationClipRecording;
using UnityEngine;

public class ExampleUsage : MonoBehaviour
{
    public HumanoidAnimationClipRecorder recorder;
    public HumanoidAnimationClipPlayer player;

    void Start()
    {
        // 記録開始
        recorder.StartRecording();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 記録停止
            var clip = recorder.StopRecording();
            
            // 再生
            if (clip != null)
            {
                player.Play(clip);
            }
        }
    }
}
```

### JSONからの読み込み

```csharp
using AnimationClipRecording;
using UnityEngine;

public class LoadFromJson : MonoBehaviour
{
    public HumanoidAnimationClipPlayer player;

    void Start()
    {
        var jsonPath = Application.persistentDataPath + "/Recordings/Json/my_motion.json";
        player.PlayFromJson(jsonPath);
    }
}
```

## AnimationClipの編集

記録されたAnimationClipはUnityの標準形式なので、UnityのAnimationウィンドウで編集できます:

1. `Window` → `Animation` → `Animation`を開く
2. 記録されたAnimationClipを選択
3. キーフレームを編集、追加、削除可能
4. カーブエディタで補間を調整可能

## Timelineとの統合

記録されたAnimationClipはUnity Timelineでも使用できます:

1. Timelineウィンドウを開く
2. アニメーショントラックを追加
3. 記録されたAnimationClipをドラッグ&ドロップ
4. Timeline上で編集・調整可能

## JSONフォーマット

JSON形式は以下の構造です:

```json
{
  "Name": "humanoid_motion",
  "Length": 5.0,
  "FrameRate": 60,
  "LoopTime": false,
  "Frames": [
    {
      "Time": 0.0,
      "RootTransform": {
        "Position": [0, 0, 0],
        "Rotation": [0, 0, 0, 1]
      },
      "BoneRotations": {
        "Hips": [0, 0, 0, 1],
        "Spine": [0, 0, 0, 1],
        ...
      }
    },
    ...
  ],
  "Metadata": {
    "RecordedAt": "2024-01-01 12:00:00",
    "BoneCount": 54,
    "RecordRootTransform": true
  }
}
```

## 既存実装との分離

この機能は以下の点で既存のPoseRuntime実装から完全に分離されています:

- **名前空間**: `AnimationClipRecording`名前空間を使用
- **独立したコンポーネント**: `HumanoidAnimationClipRecorder`と`HumanoidAnimationClipPlayer`は独立
- **データ形式**: AnimationClip形式を使用（既存の`PoseRecording`とは異なる）
- **用途**: アニメーション編集・Timeline統合を目的とした記録機能

既存の`PoseRecorder`や`PosePlayback`と併用することも可能です。

## 注意事項

- Humanoidアバターが設定されたAnimatorが必要です
- 記録はプレイモード中のみ実行可能です
- AnimationClipはHumanoidアバターのボーン構造に依存します
- JSONからの読み込みは実行時のみ可能です（エディタでは`AnimationClipConverter.LoadFromJson`を使用）

