-- Run once on existing MySQL databases before deploying password recovery.
CREATE TABLE IF NOT EXISTS `PasswordResetRequests` (
    `Id` BIGINT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `TokenHash` CHAR(64) NOT NULL,
    `RequestedAt` DATETIME NOT NULL,
    `ExpiresAt` DATETIME NOT NULL,
    `UsedAt` DATETIME NULL,
    `InvalidatedAt` DATETIME NULL,
    `DeviceType` VARCHAR(60) NULL,
    `OperatingSystem` VARCHAR(100) NULL,
    `Browser` VARCHAR(200) NULL,
    `DeviceId` VARCHAR(128) NULL,
    `PublicIp` VARCHAR(45) NULL,
    `City` VARCHAR(100) NULL,
    `Region` VARCHAR(100) NULL,
    `GpsLatitude` DECIMAL(9,6) NULL,
    `GpsLongitude` DECIMAL(9,6) NULL,
    CONSTRAINT `fk_passwordresetrequests_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    UNIQUE KEY `uq_passwordresetrequests_token_hash` (`TokenHash`),
    INDEX `idx_passwordresetrequests_user_requested` (`UserId`, `RequestedAt`),
    INDEX `idx_passwordresetrequests_expiry` (`ExpiresAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
