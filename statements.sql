SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET AUTOCOMMIT = 0;
START TRANSACTION;
SET time_zone = "+00:00";

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;


CREATE TABLE `statements` (
  `id` int(10) UNSIGNED NOT NULL COMMENT 'ID заявления',
  `status` set('free','browsing','editing') NOT NULL DEFAULT 'free' COMMENT 'Статус заявления',
  `mac` char(12) NOT NULL COMMENT 'MAC-адрес отправителя заявления',
  `datetime` datetime NOT NULL DEFAULT current_timestamp() COMMENT 'Дата и время отправки заявления',
  `name` varchar(50) NOT NULL COMMENT 'Имя заявителя',
  `surname` varchar(50) NOT NULL COMMENT 'Фамилия заявителя',
  `midname` varchar(50) NOT NULL COMMENT 'Отчество заявителя',
  `birthday` date NOT NULL COMMENT 'Дата рождения заявителя',
  `gender` tinyint(1) NOT NULL COMMENT 'Пол заявителя',
  `comment` varchar(1000) NOT NULL COMMENT 'Комменатрий заявителя'
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COMMENT='Таблица заявлений';


ALTER TABLE `statements`
  ADD PRIMARY KEY (`id`);
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
