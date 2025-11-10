# Animator Controller 条件チェックリスト

`ShadowSeatDirector`が設定するパラメータと、Animator Controller で必要な条件の対応表です。

## パラメータ一覧

### Float パラメータ

- **WalkSpeed** (Float)
  - デフォルト値: `0`
  - 設定箇所: `ShadowSeatDirector._animWalkSpeedParam = "WalkSpeed"`
  - 使用値: `_walkSpeed` (デフォルト: 2.0) または `0`

### Int パラメータ

- **SeatIndex** (Int)
  - デフォルト値: `0`
  - 設定箇所: `ShadowSeatDirector._animSeatIndexParam = "SeatIndex"`
  - 使用値: 座席のインデックス (0, 1, 2, ...)

### Bool パラメータ

- **OnFloor** (Bool)
  - デフォルト値: `false`
  - 設定箇所: `ShadowSeatDirector._animOnFloorParam = "OnFloor"`
  - 使用値: `true` (床にいる時) または `false` (座席にいる時)

### Trigger パラメータ

- **Scoot** (Trigger)

  - 設定箇所: `ShadowSeatDirector._animScootTrigger = "Scoot"`
  - 発火タイミング: 隣の椅子に人が座った時

- **Surprised** (Trigger)

  - 設定箇所: `ShadowSeatDirector._animSurprisedTrigger = "Surprised"`
  - 発火タイミング: 同じ椅子に人が座った時

- **Glare** (Trigger)

  - 設定箇所: `ShadowSeatDirector._animGlareTrigger = "Glare"`
  - 発火タイミング: 近くに人がいる時（クールダウン後）

- **Frustrated** (Trigger)

  - 設定箇所: `ShadowSeatDirector._animFrustratedTrigger = "Frustrated"`
  - 発火タイミング: 全席が埋まって床に移動する時

- **Sit** (Trigger)
  - 設定箇所: `ShadowSeatDirector._animSitTrigger = "Sit"`
  - 発火タイミング: 椅子に到着した時（歩行アニメーション終了後）

## アニメーション遷移条件

### 1. Idle → Walk

**現在の設定:**

- 条件: `WalkSpeed > 0.1`
- Has Exit Time: `true` (0.98490936)
- Transition Duration: `0.25`秒

**問題点:**

- Has Exit Time が有効なので、Idle アニメーションが 98.5%終わるまで待つ必要がある
- 即座に歩行アニメーションに遷移できない

**推奨設定:**

- 条件: `WalkSpeed > 0.1`
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.1`秒

### 2. Walk → Idle

**現在の設定:**

- 条件: `WalkSpeed < 0.1`
- Has Exit Time: `true` (0.75)
- Transition Duration: `0.25`秒

**問題点:**

- Has Exit Time が有効なので、Walk アニメーションが 75%終わるまで待つ必要がある
- 即座に Idle に戻れない

**推奨設定:**

- 条件: `WalkSpeed < 0.1`
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.2`秒

### 3. Walk → Sit

**現在の設定:**

- ❌ **遷移が存在しない**

**問題点:**

- Walk ステートから Sit ステートへの直接遷移がない
- 現在は `Walk → Idle → Sit` という 2 段階の遷移になっている可能性がある

**推奨設定:**

- Walk ステートから Sit ステートへの遷移を追加
- 条件: `Sit`トリガー
- Has Exit Time: `false`
- Transition Duration: `0.3`秒

### 4. Any State → Sit

**現在の設定:**

- 条件: `Sit`トリガー AND `OnFloor`が false
- Has Exit Time: `true` (0.75)
- Transition Duration: `0.25`秒

**問題点:**

- Has Exit Time が有効なので、現在のアニメーションが 75%終わるまで待つ
- Walk ステートから直接遷移できない可能性がある

**推奨設定:**

- 条件: `Sit`トリガー AND `OnFloor`が false
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.3`秒

### 5. Idle → Scoot

**現在の設定:**

- 条件: `Scoot`トリガー
- Has Exit Time: `true` (0.75)
- Transition Duration: `0.25`秒

**推奨設定:**

- 条件: `Scoot`トリガー
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.1`秒

### 6. Idle → Surprised

**現在の設定:**

- 条件: `Surprised`トリガー（推測、確認が必要）
- Has Exit Time: `true` (0.75)
- Transition Duration: `0.25`秒

**推奨設定:**

- 条件: `Surprised`トリガー
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.1`秒

### 7. Any State → Glare

**現在の設定:**

- 条件: `Glare`トリガー
- Has Exit Time: `false`
- Transition Duration: `0.25`秒

**状態:** ✅ 問題なし

### 8. Any State → Frustrated

**現在の設定:**

- 条件: `OnFloor`が true AND `Frustrated`トリガー
- Has Exit Time: `true` (0.75)
- Transition Duration: `0.25`秒

**推奨設定:**

- 条件: `OnFloor`が true AND `Frustrated`トリガー
- Has Exit Time: `false` (チェックを外す)
- Transition Duration: `0.1`秒

### 9. 各ステート → Idle (戻り遷移)

**現在の設定:**

- Sit → Idle: Has Exit Time: `true` (0.75)
- Scoot → Idle: Has Exit Time: `true` (0.75)
- Surprised → Idle: Has Exit Time: `true` (0.75)
- Glare → Idle: Has Exit Time: `true` (0.75)
- Frustrated → Idle: Has Exit Time: `true` (0.75)

**推奨設定:**

- すべて Has Exit Time: `true` (0.75) のままで OK
- アニメーションが終わってから Idle に戻るのは自然

## アニメーションクリップの割り当て確認

以下のステートにアニメーションクリップが割り当てられているか確認してください：

- ✅ **Idle**: アニメーションクリップあり
- ✅ **Walk**: アニメーションクリップあり
- ✅ **Sit**: アニメーションクリップあり
- ❌ **Scoot**: アニメーションクリップなし (Motion: {fileID: 0})
- ❌ **Surprised**: アニメーションクリップなし (Motion: {fileID: 0})
- ❌ **Glare**: アニメーションクリップなし (Motion: {fileID: 0})
- ❌ **Frustrated**: アニメーションクリップなし (Motion: {fileID: 0})

## 修正が必要な項目

### 優先度: 高

1. **Walk → Sit の遷移を追加**

   - Walk ステートから Sit ステートへの直接遷移がない
   - これがないと、歩行後に座るアニメーションが正しく再生されない可能性がある

2. **Idle → Walk の Has Exit Time を無効化**

   - 即座に歩行アニメーションに遷移できるようにする

3. **Walk → Idle の Has Exit Time を無効化**
   - 歩行停止時に即座に Idle に戻れるようにする

### 優先度: 中

4. **Any State → Sit の Has Exit Time を無効化**

   - Walk ステートから直接 Sit ステートに遷移できるようにする

5. **各トリガー遷移の Has Exit Time を無効化**
   - Scoot, Surprised, Frustrated の遷移を即座に実行できるようにする

### 優先度: 低

6. **アニメーションクリップの割り当て**
   - Scoot, Surprised, Glare, Frustrated にアニメーションクリップを割り当てる
   - アニメーションがない場合は、空のステートでも動作するが、視覚的な効果がない

## 確認方法

Unity エディタで以下を確認してください：

1. **Animator Controller を開く**

   - `Assets/ShadowAnim.controller` をダブルクリック

2. **各遷移の条件を確認**

   - 遷移を選択して、Inspector で条件を確認
   - Has Exit Time のチェック状態を確認

3. **パラメータ名の確認**

   - Parameters タブで、すべてのパラメータが存在するか確認
   - パラメータ名が `ShadowSeatDirector` の設定と一致しているか確認

4. **ログで確認**
   - `ShadowSeatDirector` の `Debug Log Animations` を有効にして、パラメータ変更を確認
   - Unity コンソールで、正しいパラメータが設定されているか確認
