-- Run this once on every existing MySQL database before deploying the refresh-token API.
CREATE TABLE IF NOT EXISTS `RefreshTokens` (
    `Id` BIGINT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `TokenHash` CHAR(64) NOT NULL,
    `ExpiresAt` DATETIME NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `RevokedAt` DATETIME NULL,
    `ReplacedByTokenHash` CHAR(64) NULL,
    CONSTRAINT `fk_refreshtokens_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    UNIQUE KEY `uq_refreshtokens_hash` (`TokenHash`),
    INDEX `idx_refreshtokens_user` (`UserId`),
    INDEX `idx_refreshtokens_expiry` (`ExpiresAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
