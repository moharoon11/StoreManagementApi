-- Run once before deploying the two-device session limit.
ALTER TABLE `RefreshTokens`
    ADD COLUMN `DeviceId` VARCHAR(128) NULL AFTER `UserId`,
    ADD COLUMN `DeviceName` VARCHAR(120) NULL AFTER `DeviceId`;

-- Existing refresh tokens predate device tracking. Revoking them makes the
-- transition safe: each device signs in once and receives a tracked session.
UPDATE `RefreshTokens`
SET `DeviceId` = CONCAT('legacy-', `Id`),
    `DeviceName` = 'Legacy session',
    `RevokedAt` = COALESCE(`RevokedAt`, UTC_TIMESTAMP())
WHERE `DeviceId` IS NULL;

ALTER TABLE `RefreshTokens`
    MODIFY COLUMN `DeviceId` VARCHAR(128) NOT NULL;

CREATE INDEX `idx_refresh_tokens_active_device`
    ON `RefreshTokens` (`UserId`, `DeviceId`, `RevokedAt`, `ExpiresAt`);
