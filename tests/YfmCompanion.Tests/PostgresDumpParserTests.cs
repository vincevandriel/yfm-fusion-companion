using YfmCompanion.Data;

namespace YfmCompanion.Tests;

public sealed class PostgresDumpParserTests
{
    [Fact]
    public void OnlyTopLevelInsertStatementsAreTreatedAsData()
    {
        const string sql = """
            -- INSERT INTO cards (card_id, card_name) VALUES (99, 'comment');
            /* INSERT INTO cards (card_id, card_name) VALUES (98, 'block comment'); */
            CREATE FUNCTION ignored() RETURNS void AS $$
              INSERT INTO cards (card_id, card_name) VALUES (97, 'function body');
            $$ LANGUAGE sql;
            INSERT INTO cards (card_id, card_name) VALUES
              (1, 'Real card'),
              (2, 'Text saying INSERT INTO cards (card_id) VALUES (96)');
            """;

        var batches = PostgresDumpParser.Parse(sql, new HashSet<string> { "cards" }).ToArray();

        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Rows.Length);
        Assert.Equal(1L, batch.Rows[0][0]);
        Assert.Equal("Real card", batch.Rows[0][1]);
        Assert.Equal(2L, batch.Rows[1][0]);
    }

    [Fact]
    public void HandlesEscapedQuotesSemicolonsAndMultilineText()
    {
        const string sql = """
            INSERT INTO cards (card_id, card_name) VALUES
              (1, 'Dragon''s note; still data
            on the next line');
            """;

        var batch = Assert.Single(PostgresDumpParser.Parse(sql, new HashSet<string> { "cards" }));

        Assert.Equal("Dragon's note; still data\non the next line", batch.Rows[0][1]);
    }
}
