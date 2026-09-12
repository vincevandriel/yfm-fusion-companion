namespace YfmCompanion.Data;

internal static class SqliteSchema
{
    public const string Create = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE sources (
          source_key TEXT PRIMARY KEY, repository TEXT NOT NULL, url TEXT NOT NULL,
          revision TEXT NOT NULL, role TEXT NOT NULL
        );
        CREATE TABLE cards (
          card_id INTEGER PRIMARY KEY CHECK (card_id BETWEEN 1 AND 722),
          card_name TEXT NOT NULL UNIQUE, description TEXT, guardian_star_1 TEXT,
          guardian_star_2 TEXT, level INTEGER, primary_type TEXT NOT NULL, attribute TEXT,
          attack INTEGER NOT NULL CHECK (attack >= 0), defense INTEGER NOT NULL CHECK (defense >= 0),
          password TEXT, starchip_cost INTEGER, droppable INTEGER NOT NULL CHECK (droppable IN (0,1)),
          fusible INTEGER NOT NULL CHECK (fusible IN (0,1)),
          starter_available INTEGER NOT NULL CHECK (starter_available IN (0,1))
        );
        CREATE INDEX cards_name_ci_idx ON cards (card_name COLLATE NOCASE);
        CREATE INDEX cards_primary_type_idx ON cards (primary_type);
        CREATE INDEX cards_attack_idx ON cards (attack);

        CREATE TABLE categories (
          category_id INTEGER PRIMARY KEY, category_name TEXT NOT NULL UNIQUE,
          category_kind TEXT NOT NULL CHECK (category_kind IN ('primary','secondary'))
        );
        CREATE TABLE card_categories (
          card_id INTEGER NOT NULL REFERENCES cards ON DELETE CASCADE,
          category_id INTEGER NOT NULL REFERENCES categories ON DELETE CASCADE,
          membership_kind TEXT NOT NULL CHECK (membership_kind IN ('primary','secondary')),
          PRIMARY KEY (card_id, category_id, membership_kind)
        );

        CREATE TABLE fusion_rules (
          rule_id INTEGER PRIMARY KEY, rule_kind TEXT NOT NULL CHECK (rule_kind IN ('general','exact')),
          material_1_expression TEXT NOT NULL, material_2_expression TEXT NOT NULL,
          source_order INTEGER NOT NULL UNIQUE
        );
        CREATE TABLE general_fusion_rules (
          tier_id INTEGER PRIMARY KEY, rule_id INTEGER NOT NULL REFERENCES fusion_rules ON DELETE CASCADE,
          tier_order INTEGER NOT NULL, result_card_id INTEGER NOT NULL REFERENCES cards,
          lower_attack_threshold INTEGER, result_attack_threshold INTEGER NOT NULL,
          UNIQUE (rule_id, tier_order)
        );
        CREATE TABLE exact_fusion_rules (
          rule_id INTEGER PRIMARY KEY REFERENCES fusion_rules ON DELETE CASCADE,
          result_card_id INTEGER NOT NULL REFERENCES cards
        );
        CREATE TABLE rule_index (
          rule_index_id INTEGER PRIMARY KEY, rule_id INTEGER NOT NULL REFERENCES fusion_rules ON DELETE CASCADE,
          tier_id INTEGER REFERENCES general_fusion_rules ON DELETE CASCADE,
          operand_position INTEGER NOT NULL CHECK (operand_position IN (1,2)),
          operand_expression TEXT NOT NULL,
          operand_kind TEXT NOT NULL CHECK (operand_kind IN ('category','card','set')),
          category_id INTEGER REFERENCES categories, card_id INTEGER REFERENCES cards
        );
        CREATE TABLE rule_operand_cards (
          rule_index_id INTEGER NOT NULL REFERENCES rule_index ON DELETE CASCADE,
          card_id INTEGER NOT NULL REFERENCES cards ON DELETE CASCADE,
          PRIMARY KEY (rule_index_id, card_id)
        );

        CREATE TABLE fusion_rule_conflicts (
          conflict_id INTEGER PRIMARY KEY,
          tier_id INTEGER NOT NULL REFERENCES general_fusion_rules ON DELETE CASCADE,
          precedence_order INTEGER NOT NULL,
          conflict_result_card_id INTEGER NOT NULL REFERENCES cards,
          UNIQUE (tier_id, conflict_result_card_id)
        );
        CREATE TABLE fusion_pairs (
          material_low_id INTEGER NOT NULL REFERENCES cards,
          material_high_id INTEGER NOT NULL REFERENCES cards,
          result_card_id INTEGER NOT NULL REFERENCES cards,
          is_intended INTEGER NOT NULL CHECK (is_intended IN (0,1)),
          is_glitch INTEGER NOT NULL CHECK (is_glitch IN (0,1)),
          PRIMARY KEY (material_low_id, material_high_id),
          CHECK (material_low_id <= material_high_id),
          CHECK (is_intended <> is_glitch)
        );
        CREATE INDEX fusion_pairs_result_idx ON fusion_pairs (result_card_id);
        CREATE TABLE fusion_pair_rule_refs (
          pair_rule_ref_id INTEGER PRIMARY KEY AUTOINCREMENT,
          material_low_id INTEGER NOT NULL, material_high_id INTEGER NOT NULL,
          rule_id INTEGER NOT NULL REFERENCES fusion_rules,
          tier_id INTEGER REFERENCES general_fusion_rules,
          UNIQUE (material_low_id, material_high_id, rule_id, tier_id),
          FOREIGN KEY (material_low_id, material_high_id)
            REFERENCES fusion_pairs (material_low_id, material_high_id) ON DELETE CASCADE
        );
        CREATE TABLE fusion_conflict_pairs (
          tier_id INTEGER NOT NULL REFERENCES general_fusion_rules ON DELETE CASCADE,
          material_low_id INTEGER NOT NULL, material_high_id INTEGER NOT NULL,
          actual_result_card_id INTEGER NOT NULL REFERENCES cards,
          PRIMARY KEY (tier_id, material_low_id, material_high_id),
          FOREIGN KEY (material_low_id, material_high_id)
            REFERENCES fusion_pairs (material_low_id, material_high_id) ON DELETE CASCADE
        );
        CREATE TABLE attack_rule_exceptions (
          material_low_id INTEGER NOT NULL, material_high_id INTEGER NOT NULL,
          result_card_id INTEGER NOT NULL REFERENCES cards, reason TEXT NOT NULL,
          is_glitch INTEGER NOT NULL CHECK (is_glitch IN (0,1)),
          PRIMARY KEY (material_low_id, material_high_id),
          FOREIGN KEY (material_low_id, material_high_id)
            REFERENCES fusion_pairs (material_low_id, material_high_id) ON DELETE CASCADE
        );

        CREATE TABLE equip_rules (
          equip_rule_id INTEGER PRIMARY KEY,
          equip_card_id INTEGER NOT NULL UNIQUE REFERENCES cards,
          source_order INTEGER NOT NULL UNIQUE
        );
        CREATE TABLE equip_compatibility (
          equip_rule_id INTEGER NOT NULL REFERENCES equip_rules ON DELETE CASCADE,
          equipped_card_id INTEGER NOT NULL REFERENCES cards ON DELETE CASCADE,
          PRIMARY KEY (equip_rule_id, equipped_card_id)
        );

        CREATE TABLE metadata_differences (
          difference_id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT NOT NULL,
          verified_value TEXT NOT NULL, disputed_value TEXT, evidence TEXT NOT NULL
        );
        CREATE TABLE known_disputed_checks (
          check_id INTEGER PRIMARY KEY AUTOINCREMENT, material_1 TEXT NOT NULL,
          material_2 TEXT NOT NULL, verified_result TEXT, note TEXT NOT NULL
        );
        CREATE TABLE source_manifest (
          manifest_id INTEGER PRIMARY KEY CHECK (manifest_id = 1),
          source_file_name TEXT NOT NULL,
          source_sha256 TEXT NOT NULL,
          source_length INTEGER NOT NULL,
          importer_version TEXT NOT NULL
        );
        """;
}
