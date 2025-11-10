# アニメーション遷移のタイミング

各アニメーション遷移がどのような状況で発生するかを説明します。

## 重要な注意点

**`IsAvatarMode()`が`true`の場合、アニメーションは再生されません。**

- `InteractionModeCoordinator`が`HumanoidAvatar`モードの時は、アニメーションではなくポーズデータが直接適用されます
- アニメーションが再生されるのは`ShadowInstallation`モードの時のみです

## 各遷移の発生タイミング

### 1. Idle → Walk (歩行開始)

**発生条件:**

- `WalkSpeed`パラメータが`0.1`より大きくなる
- 距離が`_minWalkDistance`（デフォルト: 1.0m）以上の場合のみ

**コード内の処理:**

```csharp
// WalkToTargetRoutine() 内
_animator.SetFloat(_animWalkSpeedParam, _walkSpeed); // デフォルト: 2.0
```

**発生タイミング:**

- 椅子への移動を開始する時
- 床から椅子に戻る時
- 別の椅子に移動する時

**具体例:**

- 現在座席 A にいて、座席 B に移動する必要がある
- 距離が 1.0m 以上の場合、歩行アニメーションが開始される
- `WalkSpeed = 2.0`が設定され、`WalkSpeed > 0.1`の条件が満たされる

---

### 2. Walk → Idle (歩行停止)

**発生条件:**

- `WalkSpeed`パラメータが`0.1`未満になる

**コード内の処理:**

```csharp
// WalkToTargetRoutine() 内、移動完了時
_animator.SetFloat(_animWalkSpeedParam, 0f);
```

**発生タイミング:**

- 椅子に到着した時
- 移動が完了した時

**具体例:**

- 歩行中に目標位置に到達
- `WalkSpeed = 0`が設定され、`WalkSpeed < 0.1`の条件が満たされる

---

### 3. Walk → Sit (歩行から座る)

**発生条件:**

- `Sit`トリガーが発火する
- `WalkSpeed`が`0`に設定された直後

**コード内の処理:**

```csharp
// WalkToTargetRoutine() 内、移動完了時
_animator.SetFloat(_animWalkSpeedParam, 0f);
TriggerAnimator(_animSitTrigger);
```

**発生タイミング:**

- 歩行アニメーションが終了し、椅子に到着した時
- **重要**: 現在、Animator Controller にこの遷移が存在しない可能性があります

**具体例:**

- 座席 B まで歩いて到着
- `WalkSpeed = 0`に設定され、`Sit`トリガーが発火
- Walk ステートから Sit ステートに遷移する必要がある

---

### 4. Any State → Sit (座る)

**発生条件:**

- `Sit`トリガーが発火する
- `OnFloor`パラメータが`false`（座席にいる状態）

**コード内の処理:**

```csharp
// MoveShadowToSeat() 内
TriggerAnimator(_animSitTrigger);
// または
// TryReturnToSeat() 内
MoveShadowToSeat(preferred, _animSitTrigger, true);
```

**発生タイミング:**

- 椅子に移動する時（`MoveShadowToSeat()`が呼ばれる時）
- 床から椅子に戻る時（`TryReturnToSeat()`が呼ばれる時）
- 初期化時、デフォルト座席に座る時

**具体例:**

- 観客が離れて、空席ができた
- 影が床から座席に戻る
- `Sit`トリガーが発火し、`OnFloor = false`が設定される

---

### 5. Idle → Scoot (隣の椅子に人が座った時)

**発生条件:**

- `Scoot`トリガーが発火する

**コード内の処理:**

```csharp
// HandleAdjacentOccupancy() 内
TriggerAnimator(_animScootTrigger);
MoveShadowToSeat(target, _animScootTrigger, true);
```

**発生タイミング:**

- **隣の椅子**（インデックスが 1 つ違い）に人が座った時
- 影が現在座っている椅子の隣に人が座った時

**具体例:**

- 影が座席 2 に座っている
- 座席 1 または座席 3 に人が座った
- `Scoot`トリガーが発火し、影が離れた座席に移動する

---

### 6. Idle → Surprised (同じ椅子に人が座った時)

**発生条件:**

- `Surprised`トリガーが発火する

**コード内の処理:**

```csharp
// HandleSeatCollision() 内
TriggerAnimator(_animSurprisedTrigger);
MoveShadowToSeat(target, _animSurprisedTrigger, true);
```

**発生タイミング:**

- **同じ椅子**に人が座った時
- 影が現在座っている椅子に人が座った時

**具体例:**

- 影が座席 2 に座っている
- 座席 2 に人が座った
- `Surprised`トリガーが発火し、影が驚いて別の座席に移動する

---

### 7. Any State → Glare (睨みつける)

**発生条件:**

- `Glare`トリガーが発火する
- クールダウン時間（デフォルト: 2.0 秒）が経過している
- 信頼度が`_glareConfidenceThreshold`（デフォルト: 0.2）以上

**コード内の処理:**

```csharp
// MaybeGlareAt() 内
if (confidence < _glareConfidenceThreshold) return;
if (Time.time - _lastGlareTime < _glareCooldown) return;
_lastGlareTime = Time.time;
TriggerAnimator(_animGlareTrigger);
```

**発生タイミング:**

- 近くに人がいるが、同じ椅子や隣の椅子ではない時
- 床にいない時（`!_onFloor`）
- 前回の睨みつけから 2 秒以上経過している時

**具体例:**

- 影が座席 2 に座っている
- 座席 4 に人が座った（隣ではない）
- 信頼度が 0.2 以上で、クールダウンが明けている
- `Glare`トリガーが発火し、影が睨みつける

---

### 8. Any State → Frustrated (困り果てる)

**発生条件:**

- `Frustrated`トリガーが発火する
- `OnFloor`パラメータが`true`（床にいる状態）

**コード内の処理:**

```csharp
// MoveShadowToFloor() 内、frustrated=true の場合
_onFloor = true;
_animator.SetBool(_animOnFloorParam, true);
TriggerAnimator(_animFrustratedTrigger);
```

**発生タイミング:**

- **全席が人で埋まった時**
- 影が床に移動する必要がある時

**具体例:**

- すべての座席に人が座っている
- 影が座る場所がない
- `OnFloor = true`が設定され、`Frustrated`トリガーが発火
- 影が床に座り込み、困り果てたアニメーションを再生

---

### 9. 各ステート → Idle (戻り遷移)

**発生条件:**

- 各アニメーションが終了した時（Has Exit Time = true）
- または、条件が満たされなくなった時

**発生タイミング:**

- **Sit → Idle**: 座るアニメーションが終了した時（Exit Time: 0.75）
- **Scoot → Idle**: Scoot アニメーションが終了した時（Exit Time: 0.75）
- **Surprised → Idle**: Surprised アニメーションが終了した時（Exit Time: 0.75）
- **Glare → Idle**: Glare アニメーションが終了した時（Exit Time: 0.75）
- **Frustrated → Idle**: Frustrated アニメーションが終了した時（Exit Time: 0.75）

---

## パラメータの設定タイミング

### WalkSpeed (Float)

| 値                         | 設定タイミング | コード箇所                     |
| -------------------------- | -------------- | ------------------------------ |
| `2.0` (または`_walkSpeed`) | 歩行開始時     | `WalkToTargetRoutine()` 開始時 |
| `0`                        | 歩行停止時     | `WalkToTargetRoutine()` 終了時 |

### OnFloor (Bool)

| 値      | 設定タイミング   | コード箇所               |
| ------- | ---------------- | ------------------------ |
| `false` | 椅子に移動する時 | `MoveShadowToSeat()` 内  |
| `true`  | 床に移動する時   | `MoveShadowToFloor()` 内 |

### SeatIndex (Int)

| 値            | 設定タイミング   | コード箇所               |
| ------------- | ---------------- | ------------------------ |
| `seat._index` | 椅子に移動する時 | `MoveShadowToSeat()` 内  |
| `seat._index` | 初期化時         | `MoveShadowInstant()` 内 |

---

## 遷移の流れの例

### 例 1: 観客が隣の椅子に座った時

```
1. Idle (待機中)
   ↓
2. Scootトリガー発火 (隣の椅子に人が座った)
   ↓
3. Idle → Scoot (Scootアニメーション再生)
   ↓
4. 移動開始 (WalkSpeed = 2.0)
   ↓
5. Scoot → Walk (WalkSpeed > 0.1 で遷移)
   ↓
6. 移動中 (Walkアニメーション再生)
   ↓
7. 到着 (WalkSpeed = 0)
   ↓
8. Walk → Idle (WalkSpeed < 0.1 で遷移)
   ↓
9. Sitトリガー発火
   ↓
10. Idle → Sit (座るアニメーション再生)
   ↓
11. Sit → Idle (アニメーション終了後)
```

### 例 2: 全席が埋まった時

```
1. Idle (待機中)
   ↓
2. 全席が埋まった検出
   ↓
3. OnFloor = true 設定
   ↓
4. Frustratedトリガー発火
   ↓
5. Any State → Frustrated (困り果てるアニメーション再生)
   ↓
6. 床への移動開始 (WalkSpeed = 2.0)
   ↓
7. Frustrated → Walk (WalkSpeed > 0.1 で遷移)
   ↓
8. 移動中 (Walkアニメーション再生)
   ↓
9. 到着 (WalkSpeed = 0)
   ↓
10. Walk → Idle (WalkSpeed < 0.1 で遷移)
```

---

## トラブルシューティング

### アニメーションが再生されない

1. **`IsAvatarMode()`を確認**

   - `InteractionModeCoordinator`が`ShadowInstallation`モードになっているか確認
   - `HumanoidAvatar`モードの場合は、アニメーションは再生されません

2. **パラメータ名を確認**

   - `ShadowSeatDirector`の`Animator Parameters`セクションで、パラメータ名が正しいか確認
   - Animator Controller のパラメータ名と一致しているか確認

3. **遷移条件を確認**

   - Animator Controller で、各遷移の条件が正しく設定されているか確認
   - Has Exit Time が適切に設定されているか確認

4. **ログで確認**
   - `Debug Log Animations`を有効にして、パラメータ変更を確認
   - Unity コンソールで、正しいパラメータが設定されているか確認
