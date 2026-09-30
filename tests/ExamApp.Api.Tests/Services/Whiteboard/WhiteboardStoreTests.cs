using System.Text.Json;
using ExamApp.Api.Services.Whiteboard;
using static ExamApp.Api.Tests.Services.Whiteboard.WhiteboardTestSupport;

namespace ExamApp.Api.Tests.Services.Whiteboard;

/// <summary>
/// issue #98 — tahta durumu: birleştirme (Excalidraw version/versionNonce kuralı + düzeltmeler), izin listesi, sınırlar,
/// version sıçraması, Join/Close yarışı, varlık (presence) sorguları, eşzamanlılık, hız sınırları.
/// </summary>
public class WhiteboardStoreTests
{
    private const int BookingId = 7;

    private static WhiteboardStore NewStore(Action<WhiteboardOptions>? configure = null)
    {
        var store = new WhiteboardStore(Options(configure));
        store.Join("conn-t", Access(BookingId));
        return store;
    }

    private static int VersionOf(JsonElement e) => e.GetProperty("version").GetInt32();
    private static int NonceOf(JsonElement e) => e.GetProperty("versionNonce").GetInt32();
    private static IReadOnlyList<JsonElement> Scene(WhiteboardStore store) => store.Join("conn-probe", Access(BookingId, 999)).Elements;

    // ---- Join / presence ----

    [Fact]
    public void Join_first_time_returns_empty_scene()
    {
        var store = new WhiteboardStore(Options());

        var outcome = store.Join("conn-1", Access(BookingId));

        outcome.Elements.ShouldBeEmpty();
        outcome.ServerVersion.ShouldBe(0);
        outcome.Previous.ShouldBeNull();
        outcome.FirstConnectionForUser.ShouldBeTrue();
        outcome.PeerOnline.ShouldBeFalse();
        store.GetMember("conn-1")!.BookingId.ShouldBe(BookingId);
        store.GetMember("conn-1")!.User.ShouldBeSameAs(TestUser);
    }

    [Fact]
    public void Join_returns_current_scene_and_version()
    {
        var store = NewStore();
        store.Merge(BookingId, [Element("a", 1), Element("b", 1)]);

        var outcome = store.Join("conn-s", Access(BookingId, 200, WhiteboardRoles.Student));

        outcome.Elements.Select(e => e.GetProperty("id").GetString()).ShouldBe(["a", "b"], ignoreOrder: true);
        outcome.ServerVersion.ShouldBe(1);
    }

    [Fact]
    public void Join_reports_peer_online_and_first_connection_per_user()
    {
        var store = NewStore(); // öğretmen (100) conn-t

        var student = store.Join("conn-s", Access(BookingId, 200, WhiteboardRoles.Student));
        student.PeerOnline.ShouldBeTrue();
        student.FirstConnectionForUser.ShouldBeTrue();

        var secondTab = store.Join("conn-t2", Access(BookingId));
        secondTab.FirstConnectionForUser.ShouldBeFalse();

        store.PeerConnectionIds(BookingId, 100).ShouldBe(["conn-s"]);
        store.PeerConnectionIds(BookingId, 200).ShouldBe(["conn-t", "conn-t2"], ignoreOrder: true);

        store.Leave("conn-t");
        store.HasUserConnection(BookingId, 100).ShouldBeTrue(); // ikinci sekme hâlâ bağlı
        store.Leave("conn-t2");
        store.HasUserConnection(BookingId, 100).ShouldBeFalse();
    }

    [Fact]
    public void Join_other_board_moves_connection_and_reports_previous()
    {
        var store = NewStore();

        var outcome = store.Join("conn-t", Access(8));

        outcome.Previous!.BookingId.ShouldBe(BookingId);
        store.GetMember("conn-t")!.BookingId.ShouldBe(8);
    }

    [Fact]
    public void Join_racing_with_close_does_not_register_on_a_closed_board()
    {
        var store = NewStore();
        store.CloseBoard(BookingId);

        // Kapanıştan sonra gelen Join yeni (boş) tahta açar — önceki sahne sızmaz.
        store.Join("conn-late", Access(BookingId)).Elements.ShouldBeEmpty();

        // Close tam ortasında: Join'in eline geçen Board nesnesi kapatılmışsa BoardClosed ve üyelik yok.
        var racing = new WhiteboardStore(Options());
        racing.Join("conn-a", Access(BookingId));
        var boards = typeof(WhiteboardStore).GetField("_boards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (System.Collections.IDictionary)boards.GetValue(racing)!;
        var board = dict[BookingId]!;
        racing.CloseBoard(BookingId);
        dict[BookingId] = board; // Join'in GetOrAdd ile kapatılmış nesneyi yakaladığı anı taklit eder

        Should.Throw<WhiteboardException>(() => racing.Join("conn-b", Access(BookingId)))
            .Code.ShouldBe(WhiteboardErrorCodes.BoardClosed);
        racing.GetMember("conn-b").ShouldBeNull();
    }

    // ---- Birleştirme ----

    [Fact]
    public void Merge_higher_version_wins_and_lower_returns_correction()
    {
        var store = NewStore();
        store.Merge(BookingId, [Element("a", 5)]);

        var older = store.Merge(BookingId, [Element("a", 4)]);
        var newer = store.Merge(BookingId, [Element("a", 6)]);

        older.Accepted.ShouldBeEmpty();
        VersionOf(older.Corrections.Single()).ShouldBe(5);
        newer.Accepted.Count.ShouldBe(1);
        newer.Corrections.ShouldBeEmpty();
        VersionOf(Scene(store).Single()).ShouldBe(6);
        newer.ServerVersion.ShouldBe(2); // yalnızca kabul edilen partiler sürümü artırır
    }

    [Fact]
    public void Merge_equal_version_smaller_versionNonce_wins_and_loser_gets_correction()
    {
        var store = NewStore();
        store.Merge(BookingId, [Element("a", 3, nonce: 500)]);

        // Eşit version, BÜYÜK nonce → kaybeder; sunucudaki kazanan düzeltme olarak döner.
        var loser = store.Merge(BookingId, [Element("a", 3, nonce: 900)]);
        loser.Accepted.ShouldBeEmpty();
        NonceOf(loser.Corrections.Single()).ShouldBe(500);

        // Eşit version, KÜÇÜK nonce → kazanır.
        var winner = store.Merge(BookingId, [Element("a", 3, nonce: 100)]);
        winner.Accepted.Count.ShouldBe(1);
        winner.Corrections.ShouldBeEmpty();
        NonceOf(Scene(store).Single()).ShouldBe(100);

        // Birebir aynı (version + nonce) → yankı: ne kabul ne düzeltme.
        var echo = store.Merge(BookingId, [Element("a", 3, nonce: 100)]);
        echo.Accepted.ShouldBeEmpty();
        echo.Corrections.ShouldBeEmpty();
    }

    [Fact]
    public void Merge_duplicate_ids_in_one_message_keeps_the_winner()
    {
        var store = NewStore();

        var result = store.Merge(BookingId,
            [Element("a", 2), Element("a", 9, nonce: 50), Element("a", 9, nonce: 10), Element("a", 3)]);

        result.Accepted.Count.ShouldBe(1);
        VersionOf(result.Accepted[0]).ShouldBe(9);
        NonceOf(result.Accepted[0]).ShouldBe(10);
    }

    [Fact]
    public void Merge_version_jump_over_10000_is_rejected()
    {
        var store = NewStore();
        store.Merge(BookingId, [Element("a", 5)]);

        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element("a", 10_006)]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
        store.Merge(BookingId, [Element("a", 10_005)]).Accepted.Count.ShouldBe(1);

        // Yeni elemanda mutlak üst sınır 10000.
        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element("b", 10_001)]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
        store.Merge(BookingId, [Element("b", 10_000)]).Accepted.Count.ShouldBe(1);

        // Hepsi ya da hiçbiri: geçerli + sıçramalı karışık mesaj sahneye dokunmaz.
        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element("c", 1), Element("a", int.MaxValue)]));
        Scene(store).Any(e => e.GetProperty("id").GetString() == "c").ShouldBeFalse();
    }

    [Fact]
    public void Merge_rejects_more_than_max_elements_per_message()
    {
        var store = NewStore();
        var elements = Enumerable.Range(0, 501).Select(i => Element($"e{i}", 1)).ToList();

        var ex = Should.Throw<WhiteboardException>(() => store.Merge(BookingId, elements));

        ex.Code.ShouldBe(WhiteboardErrorCodes.TooManyElementsInMessage);
        store.Merge(BookingId, Enumerable.Range(0, 500).Select(i => Element($"e{i}", 1)).ToList())
            .Accepted.Count.ShouldBe(500);
    }

    [Fact]
    public void Merge_scene_element_limit_rejects_without_touching_scene()
    {
        var store = NewStore(o => o.MaxSceneElements = 3);
        store.Merge(BookingId, [Element("a", 1), Element("b", 1)]);

        var ex = Should.Throw<WhiteboardException>(() =>
            store.Merge(BookingId, [Element("a", 2), Element("c", 1), Element("d", 1)]));

        ex.Code.ShouldBe(WhiteboardErrorCodes.SceneElementLimit);
        store.SceneStats(BookingId)!.Value.Count.ShouldBe(2);
        VersionOf(Scene(store).Single(e => e.GetProperty("id").GetString() == "a")).ShouldBe(1);
    }

    [Fact]
    public void Merge_scene_byte_limit_rejects_without_touching_scene()
    {
        var store = NewStore(o => o.MaxSceneBytes = 1024);
        var big = new string('x', 700);
        store.Merge(BookingId, [Element("a", 1, extra: $",\"pad\":\"{big}\"")]);

        var ex = Should.Throw<WhiteboardException>(() =>
            store.Merge(BookingId, [Element("b", 1, extra: $",\"pad\":\"{big}\"")]));

        ex.Code.ShouldBe(WhiteboardErrorCodes.SceneSizeLimit);
        store.SceneStats(BookingId)!.Value.Count.ShouldBe(1);
    }

    [Fact]
    public void Merge_replacing_element_counts_size_delta_not_sum()
    {
        var store = NewStore(o => o.MaxSceneBytes = 1024);
        var big = new string('x', 700);
        store.Merge(BookingId, [Element("a", 1, extra: $",\"pad\":\"{big}\"")]);

        store.Merge(BookingId, [Element("a", 2, extra: $",\"pad\":\"{big}\"")]).Accepted.Count.ShouldBe(1);
        var stats = store.SceneStats(BookingId)!.Value;
        stats.TotalBytes.ShouldBe(stats.ComputedBytes);
    }

    [Fact]
    public void Concurrent_merges_keep_count_and_byte_accounting_consistent()
    {
        var store = NewStore();

        // 64 paralel gönderen; id'ler kısmen çakışır (aynı id'ye farklı version'lar), hepsi farklı boyutta.
        Parallel.For(0, 64, i =>
        {
            var batch = Enumerable.Range(0, 40)
                .Select(j => Element($"e{(i * 7 + j) % 300}", 1 + (i + j) % 50, extra: $",\"pad\":\"{new string('x', (i + j) % 30)}\""))
                .ToList();
            store.Merge(BookingId, batch);
        });

        var stats = store.SceneStats(BookingId)!.Value;
        stats.Count.ShouldBe(Scene(store).Count);
        stats.Count.ShouldBeLessThanOrEqualTo(300);
        stats.TotalBytes.ShouldBe(stats.ComputedBytes);
    }

    // ---- İzin listesi / doğrulama ----

    [Theory]
    [InlineData("rectangle")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("line")]
    [InlineData("arrow")]
    [InlineData("freedraw")]
    [InlineData("text")]
    [InlineData("frame")]
    public void Merge_accepts_allowed_types(string type)
    {
        NewStore().Merge(BookingId, [Element("a", 1, type)]).Accepted.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("IMAGE")]
    [InlineData("embeddable")]
    [InlineData("iframe")]
    [InlineData("magicframe")]
    [InlineData("selection")]
    [InlineData("")]
    public void Merge_rejects_types_outside_allow_list(string type)
    {
        var store = NewStore();

        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element("ok", 1), Element("x", 1, type)]))
            .Code.ShouldBe(WhiteboardErrorCodes.ElementTypeNotAllowed);
        store.SceneStats(BookingId)!.Value.Count.ShouldBe(0); // hepsi ya da hiçbiri
    }

    [Fact]
    public void Merge_rejects_file_references()
    {
        Should.Throw<WhiteboardException>(() => NewStore().Merge(BookingId, [Element("f", 1, extra: ",\"fileId\":\"abc\"")]))
            .Code.ShouldBe(WhiteboardErrorCodes.ImagesNotSupported);
        NewStore().Merge(BookingId, [Element("f", 1, extra: ",\"fileId\":null")]).Accepted.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(",\"link\":\"javascript:alert(1)\"")]
    [InlineData(",\"link\":\"data:text/html,x\"")]
    [InlineData(",\"link\":\"ftp://example.com\"")]
    [InlineData(",\"link\":\"/relative\"")]
    [InlineData(",\"link\":42")]
    [InlineData(",\"customData\":{\"a\":1}")]
    [InlineData(",\"customData\":null")]
    public void Merge_rejects_unsafe_link_and_customData(string extra)
    {
        Should.Throw<WhiteboardException>(() => NewStore().Merge(BookingId, [Element("a", 1, extra: extra)]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
    }

    [Theory]
    [InlineData(",\"link\":null")]
    [InlineData(",\"link\":\"https://example.com/a?b=c\"")]
    [InlineData(",\"link\":\"http://example.com\"")]
    public void Merge_accepts_http_links_and_null(string extra)
    {
        NewStore().Merge(BookingId, [Element("a", 1, extra: extra)]).Accepted.Count.ShouldBe(1);
    }

    [Fact]
    public void Merge_rejects_link_longer_than_2048()
    {
        var ok = "https://e.com/" + new string('a', 2048 - 14);
        NewStore().Merge(BookingId, [Element("a", 1, extra: $",\"link\":\"{ok}\"")]).Accepted.Count.ShouldBe(1);
        Should.Throw<WhiteboardException>(() => NewStore().Merge(BookingId, [Element("a", 1, extra: $",\"link\":\"{ok}a\"")]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"id\":\"a\",\"version\":1}")]
    [InlineData("{\"type\":\"rectangle\",\"version\":1}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":5,\"version\":1}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"\",\"version\":1}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"a\"}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"a\",\"version\":\"1\"}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"a\",\"version\":1.5}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"a\",\"version\":-1}")]
    [InlineData("{\"type\":\"rectangle\",\"id\":\"a\",\"version\":1,\"versionNonce\":\"x\"}")]
    [InlineData("{\"type\":5,\"id\":\"a\",\"version\":1}")]
    public void Merge_rejects_invalid_elements(string json)
    {
        Should.Throw<WhiteboardException>(() => NewStore().Merge(BookingId, [Raw(json)]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
    }

    [Fact]
    public void Merge_rejects_id_longer_than_64()
    {
        var store = NewStore();

        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element(new string('a', 65), 1)]))
            .Code.ShouldBe(WhiteboardErrorCodes.InvalidElement);
        store.Merge(BookingId, [Element(new string('a', 64), 1)]).Accepted.Count.ShouldBe(1);
    }

    [Fact]
    public void Merge_on_closed_board_throws_BoardClosed()
    {
        var store = NewStore();
        store.CloseBoard(BookingId);

        Should.Throw<WhiteboardException>(() => store.Merge(BookingId, [Element("a", 1)]))
            .Code.ShouldBe(WhiteboardErrorCodes.BoardClosed);
    }

    // ---- Ayrılma / kapanış ----

    [Fact]
    public void Leave_keeps_scene_for_rejoin_within_window()
    {
        var store = NewStore();
        store.Merge(BookingId, [Element("a", 1)]);

        store.Leave("conn-t");
        store.GetMember("conn-t").ShouldBeNull();
        store.IsOpen(BookingId).ShouldBeTrue();

        store.Join("conn-t2", Access(BookingId)).Elements.Count.ShouldBe(1);
    }

    [Fact]
    public void CloseBoard_removes_state_and_returns_member_connections()
    {
        var store = NewStore();
        store.Join("conn-s", Access(BookingId, 200, WhiteboardRoles.Student));
        store.Join("conn-other", Access(99));

        var connections = store.CloseBoard(BookingId);

        connections.ShouldBe(["conn-t", "conn-s"], ignoreOrder: true);
        store.IsOpen(BookingId).ShouldBeFalse();
        store.GetMember("conn-t").ShouldBeNull();
        store.GetMember("conn-other").ShouldNotBeNull();
        store.CloseBoard(BookingId).ShouldBeNull();
    }

    // ---- Hız sınırları ----

    [Fact]
    public void Message_rate_limit_allows_30_per_second_then_refills()
    {
        var store = new WhiteboardStore(Options());

        for (var i = 0; i < 30; i++)
            store.TryAcquireMessage("sub-1", Now).ShouldBeTrue();
        store.TryAcquireMessage("sub-1", Now).ShouldBeFalse();

        store.TryAcquireMessage("sub-2", Now).ShouldBeTrue(); // kullanıcı başına

        var later = Now.AddMilliseconds(100); // ~3 jeton
        for (var i = 0; i < 3; i++)
            store.TryAcquireMessage("sub-1", later).ShouldBeTrue();
        store.TryAcquireMessage("sub-1", later).ShouldBeFalse();

        var much = Now.AddMinutes(1); // kapasiteyi aşmaz
        for (var i = 0; i < 30; i++)
            store.TryAcquireMessage("sub-1", much).ShouldBeTrue();
        store.TryAcquireMessage("sub-1", much).ShouldBeFalse();
    }

    [Fact]
    public void Join_rate_limit_is_2_per_second_with_burst_4_and_separate_from_messages()
    {
        var store = new WhiteboardStore(Options());
        for (var i = 0; i < 30; i++)
            store.TryAcquireMessage("sub-1", Now);

        for (var i = 0; i < 4; i++)
            store.TryAcquireJoin("sub-1", Now).ShouldBeTrue();
        store.TryAcquireJoin("sub-1", Now).ShouldBeFalse();

        var half = Now.AddMilliseconds(500); // 1 jeton
        store.TryAcquireJoin("sub-1", half).ShouldBeTrue();
        store.TryAcquireJoin("sub-1", half).ShouldBeFalse();

        var later = Now.AddMinutes(1); // patlama kapasitesi 4
        for (var i = 0; i < 4; i++)
            store.TryAcquireJoin("sub-1", later).ShouldBeTrue();
        store.TryAcquireJoin("sub-1", later).ShouldBeFalse();
    }

    [Fact]
    public void Pointer_rate_limit_is_separate_and_20_per_second()
    {
        var store = new WhiteboardStore(Options());
        for (var i = 0; i < 30; i++)
            store.TryAcquireMessage("sub-1", Now);

        for (var i = 0; i < 20; i++)
            store.TryAcquirePointer("sub-1", Now).ShouldBeTrue();
        store.TryAcquirePointer("sub-1", Now).ShouldBeFalse();
    }
}
