-- Run once on existing MySQL databases before deploying OTP password reset.
-- PasswordResetRequests is a legacy link-reset table and is intentionally left
-- untouched so existing deployments retain their audit data.
CREATE TABLE IF NOT EXISTS `PasswordResetOtps` (
    `Id` BIGINT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `Username` VARCHAR(100) NOT NULL,
    `Email` VARCHAR(150) NOT NULL,
    `OtpHmacHash` CHAR(64) NOT NULL,
    `ResetTokenHmacHash` CHAR(64) NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ExpiresAt` DATETIME NOT NULL,
    `FailedAttempts` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `VerifiedAt` DATETIME NULL,
    `UsedAt` DATETIME NULL,
    `InvalidatedAt` DATETIME NULL,
    CONSTRAINT `fk_passwordresetotps_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    UNIQUE KEY `uq_passwordresetotps_reset_token_hmac_hash` (`ResetTokenHmacHash`),
    INDEX `idx_passwordresetotps_user_created` (`UserId`, `CreatedAt`),
    INDEX `idx_passwordresetotps_expiry` (`ExpiresAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
