-- Store Management, Billing and Stock Management Database Schema
-- Compatible with MySQL 8.0+

CREATE DATABASE IF NOT EXISTS `store_management_db` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `store_management_db`;

-- 1. Users Table
CREATE TABLE IF NOT EXISTS `Users` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `Username` VARCHAR(100) NOT NULL UNIQUE,
    `PasswordHash` VARCHAR(255) NOT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. StoreProfiles Table
CREATE TABLE IF NOT EXISTS `StoreProfiles` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL UNIQUE,
    `StoreName` VARCHAR(150) NOT NULL,
    `OwnerName` VARCHAR(150) NOT NULL,
    `LogoUrl` VARCHAR(500) DEFAULT NULL,
    `Address` TEXT DEFAULT NULL,
    `City` VARCHAR(100) DEFAULT NULL,
    `District` VARCHAR(100) DEFAULT NULL,
    `Pincode` VARCHAR(20) DEFAULT NULL,
    `Email` VARCHAR(150) DEFAULT NULL,
    `GstNumber` VARCHAR(50) DEFAULT NULL,
    `Phone` VARCHAR(20) DEFAULT NULL,
    `AlternatePhone` VARCHAR(20) DEFAULT NULL,
    `AboutUs` TEXT DEFAULT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT `fk_storeprofiles_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 3. Categories Table
CREATE TABLE IF NOT EXISTS `Categories` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `Name` VARCHAR(100) NOT NULL,
    `ImageUrl` VARCHAR(500) DEFAULT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT `fk_categories_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    INDEX `idx_categories_user` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 4. Products Table
CREATE TABLE IF NOT EXISTS `Products` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `CategoryId` INT NOT NULL,
    `Name` VARCHAR(150) NOT NULL,
    `ImageUrl` VARCHAR(500) DEFAULT NULL,
    `CostPrice` DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    `SellingPrice` DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    `StockQuantity` INT NOT NULL DEFAULT 0,
    `SoldsCount` INT NOT NULL DEFAULT 0,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT `fk_products_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_products_category` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE RESTRICT,
    INDEX `idx_products_user` (`UserId`),
    INDEX `idx_products_category` (`CategoryId`),
    INDEX `idx_products_name` (`Name`),
    INDEX `idx_products_solds` (`SoldsCount` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 5. Favourites Table
CREATE TABLE IF NOT EXISTS `Favourites` (
    `UserId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`UserId`, `ProductId`),
    CONSTRAINT `fk_favourites_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_favourites_product` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 6. Invoices Table
CREATE TABLE IF NOT EXISTS `Invoices` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `InvoiceNumber` VARCHAR(50) NOT NULL UNIQUE,
    `Subtotal` DECIMAL(18,2) NOT NULL,
    `GrandTotal` DECIMAL(18,2) NOT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT `fk_invoices_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    INDEX `idx_invoices_user_date` (`UserId`, `CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 7. InvoiceItems Table
CREATE TABLE IF NOT EXISTS `InvoiceItems` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `InvoiceId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `ProductName` VARCHAR(150) NOT NULL,
    `Quantity` INT NOT NULL,
    `SellingPrice` DECIMAL(18,2) NOT NULL,
    `Total` DECIMAL(18,2) NOT NULL,
    CONSTRAINT `fk_invoiceitems_invoice` FOREIGN KEY (`InvoiceId`) REFERENCES `Invoices` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_invoiceitems_product` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 8. StockMovements Table
CREATE TABLE IF NOT EXISTS `StockMovements` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `UserId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `PreviousQuantity` INT NOT NULL,
    `QuantityChanged` INT NOT NULL,
    `NewQuantity` INT NOT NULL,
    `Reason` ENUM('SALE', 'STOCK_ADDED', 'MANUAL_ADJUSTMENT', 'RETURN') NOT NULL,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT `fk_stockmovements_user` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_stockmovements_product` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
    INDEX `idx_stockmovements_user` (`UserId`, `ProductId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
