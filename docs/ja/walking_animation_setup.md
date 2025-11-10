# 歩行アニメーション設定ガイド

`ShadowSeatDirector`に歩行アニメーション機能を追加しました。このガイドでは、Unity エディタ側で必要な設定手順を説明します。

## 1. Animator Controller の設定

### 1.1 WalkSpeed パラメータの追加

1. `ShadowAnim.controller`を開きます（`Assets/ShadowAnim.controller`）
2. **Parameters**タブを開きます
3. **+**ボタンをクリックして**Float**を選択
4. パラメータ名を`WalkSpeed`に設定
5. デフォルト値を`0`に設定

### 1.2 歩行アニメーションステートの追加

1. Animator ウィンドウで**Base Layer**を選択
2. 右クリック → **Create State** → **Empty**を選択
3. 新しいステートを`Walk`にリネーム
4. `Walk`ステートを選択し、**Motion**フィールドに歩行アニメーションクリップを割り当て
   - 歩行アニメーションがない場合は、後述の「アニメーションの準備」を参照

### 1.3 ステート遷移の設定

#### Idle → Walk の遷移

1. `Idle`ステートを選択
2. 右クリック → **Make Transition** → `Walk`を選択
3. 遷移を選択し、**Conditions**に以下を追加：
   - `WalkSpeed` **Greater** `0.1`
4. **Settings**で以下を設定：
   - **Has Exit Time**: チェックを外す
   - **Transition Duration**: `0.1`秒程度

#### Walk → Idle の遷移

1. `Walk`ステートを選択
2. 右クリック → **Make Transition** → `Idle`を選択
3. 遷移を選択し、**Conditions**に以下を追加：
   - `WalkSpeed` **Less** `0.1`
4. **Settings**で以下を設定：
   - **Has Exit Time**: チェックを外す
   - **Transition Duration**: `0.2`秒程度

#### Walk → Sit の遷移（オプション）

椅子に到着した時に座るアニメーションに遷移させたい場合：

1. `Walk`ステートを選択
2. 右クリック → **Make Transition** → `Sit`ステートを選択
3. 遷移を選択し、**Conditions**に以下を追加：
   - `Sit`トリガー
4. **Settings**で以下を設定：
   - **Has Exit Time**: チェックを外す
   - **Transition Duration**: `0.3`秒程度

## 2. ShadowSeatDirector コンポーネントの設定

1. シーン内の`ShadowSeatDirector`コンポーネントを選択
2. **Walking Animation**セクションで以下を設定：
   - **Use Walking Animation**: チェックを入れる（デフォルトで有効）
   - **Walk Speed**: `2.0`（歩行速度、メートル/秒）
   - **Walk Rotation Speed**: `5.0`（回転速度）
   - **Stopping Distance**: `0.5`（停止距離、メートル）
   - **Min Walk Distance**: `1.0`（この距離以上の場合のみ歩行アニメーションを使用）
3. **Animator Parameters**セクションで以下を確認：
   - **Anim Walk Speed Param**: `WalkSpeed`（Animator Controller のパラメータ名と一致させる）

## 3. アニメーションの準備

### 3.1 既存のアニメーションを使用する場合

Unity 標準の Humanoid アニメーションやアセットストアのアニメーションを使用できます：

1. **Window** → **Animation** → **Animation**を開く
2. アバターの GameObject を選択
3. **Create**ボタンをクリックして新しい Animation Clip を作成
4. または、既存の歩行アニメーションをインポート

### 3.2 アニメーションクリップの設定

歩行アニメーションクリップを`Walk`ステートに割り当てる際の推奨設定：

- **Loop Time**: チェックを入れる（ループ再生）
- **Root Transform Rotation**: **Bake Into Pose**を選択（回転はコードで制御）
- **Root Transform Position (Y)**: **Bake Into Pose**を選択（Y 軸位置は固定）
- **Root Transform Position (XZ)**: **Bake Into Pose**を選択（XZ 軸位置はコードで制御）

### 3.3 アニメーションの取得方法

以下の方法で歩行アニメーションを取得できます：

1. **Unity Asset Store**:

   - "Humanoid Walk" や "Character Animation Pack" を検索
   - 無料のアセットも多数あります

2. **Mixamo** (https://www.mixamo.com):

   - 無料の 3D キャラクターアニメーション
   - "Walk" で検索してダウンロード
   - FBX 形式で Unity にインポート

3. **自分で作成**:
   - Blender や Maya などの 3D ソフトで作成
   - Unity の Animation ウィンドウで手動でキーフレームを作成

## 4. 動作確認

1. シーンを再生します
2. `ShadowSeatDirector`が椅子への移動を開始すると、歩行アニメーションが再生されます
3. 椅子に到着すると、`WalkSpeed`が`0`に設定され、`Sit`トリガーが発火して座るアニメーションに遷移します

## 5. トラブルシューティング

### 歩行アニメーションが再生されない

- `Use Walking Animation`が有効になっているか確認
- `WalkSpeed`パラメータが Animator Controller に存在するか確認
- パラメータ名が`ShadowSeatDirector`の`Anim Walk Speed Param`と一致しているか確認
- 距離が`Min Walk Distance`以上か確認

### アニメーションが滑る（スライディング）

- アニメーションクリップの**Root Transform Position (XZ)**を**Bake Into Pose**に設定
- `Walk Speed`を調整して、アニメーションの速度と一致させる

### 回転が不自然

- `Walk Rotation Speed`を調整（値が大きいほど素早く回転）
- アニメーションクリップの**Root Transform Rotation**を**Bake Into Pose**に設定

### 座るアニメーションに遷移しない

- `Sit`トリガーが Animator Controller に存在するか確認
- `Walk` → `Sit`の遷移が正しく設定されているか確認

## 6. パラメータの調整

以下のパラメータを調整して、動作を微調整できます：

- **Walk Speed**: 歩行速度（デフォルト: 2.0 m/s）
- **Walk Rotation Speed**: 回転速度（デフォルト: 5.0）
- **Stopping Distance**: 停止距離（デフォルト: 0.5 m）
- **Min Walk Distance**: 歩行アニメーションを使用する最小距離（デフォルト: 1.0 m）

距離が`Min Walk Distance`未満の場合は、従来の Lerp 移動が使用されます。
