# Animation 録画トラブルシューティング

HumanoidAvatar 用の AnimationClip が生成されない問題の解決方法です。

## 問題の原因

`HumanoidAnimationClipRecorder`には 2 つの録画モードがあります：

1. **Humanoid Muscle Curves モード** (`_useHumanoidMuscleCurves = true`)

   - HumanoidAvatar 用の AnimationClip を生成
   - `RootT.x/y/z`, `RootQ.x/y/z/w`, 筋肉カーブ (`HumanTrait.MuscleName`) を使用

2. **Transform モード** (`_useHumanoidMuscleCurves = false`)
   - Generic/Transform 用の AnimationClip を生成
   - `localPosition`, `localRotation`, `localScale` を使用

## 確認すべき設定

### 1. `_useHumanoidMuscleCurves` の設定

**Unity エディタで確認:**

1. `HumanoidAnimationClipRecorder`コンポーネントを選択
2. **Use Humanoid Muscle Curves** にチェックが入っているか確認
3. チェックが外れている場合は、**チェックを入れる**

**デフォルト値:** `false`（Transform モード）

### 2. Animator の Avatar 設定

**Unity エディタで確認:**

1. アバターの GameObject を選択
2. `Animator`コンポーネントを確認
3. **Avatar**フィールドに HumanoidAvatar が割り当てられているか確認
4. **Avatar Type**が`Humanoid`になっているか確認

**確認方法:**

- Avatar を選択して、Inspector で`Avatar Type`を確認
- `Humanoid`になっていない場合は、Model Import Settings で`Animation Type`を`Humanoid`に設定

### 3. Avatar の Humanoid 設定

**Unity エディタで確認:**

1. モデルファイル（.fbx など）を選択
2. **Rig**タブを開く
3. **Animation Type**が`Humanoid`になっているか確認
4. **Avatar Definition**が`Create From This Model`になっているか確認
5. **Configure...**ボタンをクリックして、ボーンマッピングが正しいか確認

### 4. コード内のチェック

`HumanoidAnimationClipRecorder`は以下の条件をチェックしています：

```csharp
// Awake() 内
if (_animator.avatar == null || !_animator.avatar.isHuman)
{
    Debug.LogWarning("HumanoidAnimationClipRecorder: Animator with Humanoid Avatar is recommended");
}

// EnsureHumanPoseHandler() 内
if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
{
    Debug.LogError("HumanoidAnimationClipRecorder: Humanoid Animator with a valid Avatar is required");
    return false;
}
```

**確認方法:**

- Unity コンソールで警告やエラーメッセージを確認
- `Debug Logging`を有効にして、詳細なログを確認

## 解決方法

### 方法 1: `_useHumanoidMuscleCurves`を有効にする

1. `HumanoidAnimationClipRecorder`コンポーネントを選択
2. **Use Humanoid Muscle Curves** にチェックを入れる
3. 録画を再実行

### 方法 2: Avatar の設定を確認・修正する

1. モデルファイルを選択
2. **Rig**タブで`Animation Type`を`Humanoid`に設定
3. **Apply**をクリック
4. Avatar が自動生成されるのを待つ
5. `Animator`コンポーネントに Avatar が割り当てられているか確認

### 方法 3: 既存の Avatar を再設定する

1. `Animator`コンポーネントを選択
2. **Avatar**フィールドをクリア
3. モデルファイルの**Rig**タブで`Animation Type`を`Humanoid`に設定
4. **Apply**をクリック
5. 生成された Avatar を`Animator`に割り当て

## 録画モードの違い

### Humanoid Muscle Curves モード（推奨）

**生成される AnimationClip:**

- `RootT.x/y/z`: ルート位置
- `RootQ.x/y/z/w`: ルート回転
- `HumanTrait.MuscleName[i]`: 各筋肉の値（95 個程度）

**利点:**

- HumanoidAvatar 間で互換性が高い
- 異なるモデルでも同じアニメーションを適用できる
- Unity の Humanoid システムと完全に統合

**使用条件:**

- `_useHumanoidMuscleCurves = true`
- `Animator.avatar != null`
- `Animator.avatar.isHuman == true`

### Transform モード

**生成される AnimationClip:**

- `localPosition.x/y/z`: 各 Transform のローカル位置
- `localRotation.x/y/z/w`: 各 Transform のローカル回転
- `localScale.x/y/z`: 各 Transform のローカルスケール

**利点:**

- 特定のモデルに特化したアニメーション
- より細かい制御が可能

**欠点:**

- 異なるモデルでは動作しない可能性がある
- HumanoidAvatar の利点を活かせない

## デバッグ方法

### 1. ログを確認

`HumanoidAnimationClipRecorder`の`Debug Logging`を有効にして、以下のログを確認：

```
HumanoidAnimationClipRecorder: Started recording humanoid muscle curves at 60 fps
```

または

```
HumanoidAnimationClipRecorder: Started recording X transforms at 60 fps
```

### 2. 録画後のログを確認

録画停止後のログを確認：

```
HumanoidAnimationClipRecorder: Recorded X humanoid frames. Clip length X.XXXs @ 60 fps capturing 95 muscles.
```

または

```
HumanoidAnimationClipRecorder: Recorded X frames across Y transforms...
```

### 3. AnimationClip の内容を確認

Unity エディタで：

1. 録画された AnimationClip を選択
2. **Inspector**で確認
3. **Curves**セクションで、以下のいずれかが表示されるか確認：
   - `RootT.x/y/z`, `RootQ.x/y/z/w`, 筋肉カーブ → Humanoid モード
   - `localPosition`, `localRotation` → Transform モード

## よくある問題

### 問題 1: `_useHumanoidMuscleCurves`が`true`なのに Transform モードになる

**原因:**

- `EnsureHumanPoseHandler()`が失敗している
- `Animator.avatar`が`null`または`isHuman == false`

**解決方法:**

1. `Animator`コンポーネントに Avatar が割り当てられているか確認
2. Avatar が Humanoid タイプか確認
3. Unity コンソールでエラーメッセージを確認

### 問題 2: Avatar は設定されているが、録画が失敗する

**原因:**

- Avatar のボーンマッピングが不完全
- `HumanPoseHandler`の初期化に失敗

**解決方法:**

1. モデルファイルの**Rig**タブで**Configure...**をクリック
2. ボーンマッピングが正しいか確認
3. 赤い警告がないか確認
4. `Debug Logging`を有効にして、詳細なエラーを確認

### 問題 3: 録画は成功するが、AnimationClip が正しく再生されない

**原因:**

- AnimationClip の設定が間違っている
- Avatar の設定が変更された

**解決方法:**

1. AnimationClip を選択して、Inspector で確認
2. **Root Transform Rotation**や**Root Transform Position**の設定を確認
3. Avatar の設定が録画時と同じか確認

## 推奨設定

HumanoidAvatar 用の AnimationClip を録画する場合の推奨設定：

```
Use Humanoid Muscle Curves: ✓ (true)
Record Root Transform: ✓ (true)
Record Entire Hierarchy: ✓ (true) または選択したボーンのみ
Record Local Positions: ✓ (true) - Humanoidモードでは使用されない
Record Local Rotations: ✓ (true) - Humanoidモードでは使用されない
Record Local Scale: ✗ (false)
```

**注意:** `_useHumanoidMuscleCurves = true`の場合、`Record Local Positions/Rotations/Scale`の設定は無視されます（Humanoid Muscle Curves が使用されるため）。
