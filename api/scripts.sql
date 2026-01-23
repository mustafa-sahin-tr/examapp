DO $$
DECLARE
    -- GİRİLEN DEĞERLER
    v_topic_name     text := 'Okuma';
    v_subtopic_name  text := 'Okuduğunu Anlama';
    v_grade_name     text := '4. Sınıf';
    v_subject_name   text := 'Türkçe';

    -- HESAPLANAN DEĞERLER
    v_subject_id     bigint;
    v_grade_id       bigint;
    v_topic_id       bigint;
BEGIN

    -- 1️⃣ SubjectId ve GradeId bul
    SELECT 
        s."Id",
        g."Id"
    INTO 
        v_subject_id,
        v_grade_id
    FROM public."GradeSubjects" gs
    JOIN public."Grades" g   ON g."Id" = gs."GradeId"
    JOIN public."Subjects" s ON s."Id" = gs."SubjectId"
    WHERE g."Name" = v_grade_name
      AND s."Name" = v_subject_name
      AND g."IsDeleted" = FALSE
      AND s."IsDeleted" = FALSE;

    IF v_subject_id IS NULL OR v_grade_id IS NULL THEN
        RAISE EXCEPTION 'Subject veya Grade bulunamadı';
    END IF;

    -- 2️⃣ Topic var mı kontrol et
    SELECT t."Id"
    INTO v_topic_id
    FROM public."Topics" t
    WHERE t."Name" = v_topic_name
      AND t."SubjectId" = v_subject_id
      AND t."GradeId" = v_grade_id
    LIMIT 1;

    -- 3️⃣ Topic yoksa oluştur
    IF v_topic_id IS NULL THEN
        SELECT COALESCE(MAX("Id"), 0) + 1
        INTO v_topic_id
        FROM public."Topics";

        INSERT INTO public."Topics" (
            "Id",
            "Name",
            "SubjectId",
            "GradeId"
        )
        VALUES (
            v_topic_id,
            v_topic_name,
            v_subject_id,
            v_grade_id
        );

        RAISE NOTICE 'Yeni Topic oluşturuldu. TopicId=%', v_topic_id;
    ELSE
        RAISE NOTICE 'Topic zaten mevcut. TopicId=%', v_topic_id;
    END IF;

    -- 4️⃣ SubTopic ekle (her durumda)
    INSERT INTO public."SubTopics" (
        "Id",
        "Name",
        "TopicId"
    )
    VALUES (
        (SELECT COALESCE(MAX("Id"), 0) + 1 FROM public."SubTopics"),
        v_subtopic_name,
        v_topic_id
    );

    RAISE NOTICE 'SubTopic eklendi. TopicId=%', v_topic_id;

END $$;
