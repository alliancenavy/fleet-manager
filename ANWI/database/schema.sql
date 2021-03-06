﻿pragma foreign_keys = "1";

drop table if exists HullVendor;

create table if not exists HullVendor (
 id integer not null primary key autoincrement,
 name text not null unique,
 abrv text not null unique,
 icon text not null
);

drop table if exists HullRole;

create table if not exists HullRole (
 id integer not null primary key autoincrement,
 name text not null unique,
 icon text not null
);

drop table if exists Hull;

create table if not exists Hull (
 id integer not null primary key autoincrement,
 vendor integer not null references HullVendor(id),
 role integer not null references HullRole(id),
 series text not null,
 symbol text not null default '',
 ordering integer not null default 0
);

drop table if exists Rank;

create table if not exists Rank (
 id integer not null primary key autoincrement,
 name text not null unique,
 abrv text not null unique,
 icon text not null default '',
 ordering integer not null default 0
);

drop table if exists Rate;

create table if not exists Rate (
 id integer not null primary key autoincrement,
 name text not null unique,
 abrv text not null unique,
 rank2duration integer default 31556926,
 rank1duration integer default 15778463
);

drop table if exists AssignmentRole;

create table if not exists AssignmentRole (
 id integer not null primary key autoincrement,
 name text not null unique,
 isCompany integer not null
);

DROP TABLE IF EXISTS OperationRole;

CREATE TABLE IF NOT EXISTS OperationRole (
 id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
 name TEXT NOT NULL UNIQUE,
 associatedRate INTEGER NOT NULL REFERENCES Rate(id),
 onShips INTEGER NOT NULL,
 onBoats INTEGER NOT NULL,
 inSquads INTEGER NOT NULL,
 channelCdr INTEGER NOT NULL
);

drop table if exists StruckRate;

CREATE TABLE StruckRate (
    id      INTEGER NOT NULL UNIQUE,
    user    INTEGER NOT NULL,
    rate    INTEGER NOT NULL,
    rank    INTEGER NOT NULL DEFAULT 0,
    earned  INTEGER NOT NULL DEFAULT 0,
    expires INTEGER DEFAULT NULL,
    PRIMARY KEY (user,rate),
    FOREIGN KEY (user) REFERENCES User (id),
    FOREIGN KEY (rate) REFERENCES Rate (id) 
);

drop table if exists User;

create table if not exists User (
 id integer not null primary key autoincrement,
 name text not null unique,
 auth0 text not null default '',
 rank integer not null references Rank(id),
 rate integer references StruckRate(id),
 created integer not null
);

drop table if exists Assignment;

create table if not exists Assignment (
 id integer not null primary key autoincrement,
 user integer not null references User(id),
 ship integer not null references UserShip(id),
 role integer not null references AssignmentRole(id),
 start integer not null default 0,
 until integer default null
);

drop table if exists UserShip;

create table if not exists UserShip (
 id integer not null primary key autoincrement,
 user integer not null references User(id),
 hull integer not null references Hull(id),
 insurance integer not null default 0,
 number integer not null default 0,
 name text not null,
 status integer not null default 0,
 statusDate integer not null default 0,
 final integer not null default 0
);

DROP TABLE IF EXISTS ShipEquipment;

CREATE TABLE IF NOT EXISTS ShipEquipment (
	id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	hull INTEGER NOT NULL REFERENCES Hull(id),
	ship INTEGER NOT NULL REFERENCES UserShip(id)
);

drop table if exists UserPrivs;

create table if not exists UserPrivs (
 user integer not null references User(id),
 canPromote integer not null default 0,
 canCertify integer not null default 0,
 canAssign integer not null default 0,
 canStartOps integer not null default 0,
 isFleetAdmin integer not null default 0
);

CREATE TRIGGER on_primary_rate_delete AFTER DELETE ON StruckRate BEGIN
  UPDATE User SET rate = null WHERE rate = old.id;
END;

CREATE TRIGGER new_user_privs AFTER INSERT ON User BEGIN
	INSERT INTO UserPrivs (user, canPromote, canCertify, canAssign, canStartOps, isFleetAdmin) VALUES (new.id,0,0,0,0,0);
END;