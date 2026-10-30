module Tests.Completion

// The completion triggers and the completion surgery, kept in step
// with the site's own rules: what the caret sits in, and what a pick
// leaves in the text.

open System
open Expecto
open DevNull.Client.Core.Completion

let tests =
  testList "completion" [
    testCase
      "a bare @ at the start of the text triggers an empty query"
      (fun () ->
        Expect.equal (ValueSome(Mention(0, ""))) (triggerAt("@", 1)) "a bare @")

    testCase "an @ glued to a word never triggers" (fun () ->
      Expect.equal ValueNone (triggerAt("a@kai", 5)) "glued to a word"

      Expect.equal ValueNone (triggerAt("hi_@kai", 7)) "after an underscore"

      Expect.equal ValueNone (triggerAt("hi/@kai", 7)) "after a slash")

    testCase "an @ mid-text triggers with the query before the caret" (fun () ->
      Expect.equal
        (ValueSome(Mention(3, "k")))
        (triggerAt("hi @kai", 5))
        "the query stops at the caret"

      Expect.equal
        (ValueSome(Mention(3, "kai")))
        (triggerAt("hi @kai!", 7))
        "the query stops before the punctuation")

    testCase
      "a query over the thirty-nine handle characters never triggers"
      (fun () ->
        let typed = "@" + String('k', 40)

        Expect.equal
          ValueNone
          (triggerAt(typed, typed.Length))
          "over the handle bound")

    testCase "a number after a hash triggers an issue reference" (fun () ->
      Expect.equal
        (ValueSome(IssueRef(4, "12")))
        (triggerAt("see #12", 7))
        "in prose"

      Expect.equal
        (ValueSome(IssueRef(0, "12")))
        (triggerAt("#12", 3))
        "at the start"

      Expect.equal
        (ValueSome(IssueRef(5, "12")))
        (triggerAt("see (#12", 8))
        "after an opening bracket")

    testCase
      "a number never opens with a zero or runs past ten digits"
      (fun () ->
        Expect.equal ValueNone (triggerAt("#012", 4)) "a leading zero"

        Expect.equal ValueNone (triggerAt("#12345678901", 12)) "eleven digits")

    testCase
      "a number glued to a word or followed by one never triggers"
      (fun () ->
        Expect.equal ValueNone (triggerAt("a#12", 4)) "glued before"

        Expect.equal
          ValueNone
          (triggerAt("see #12a", 7))
          "a word after the caret")

    testCase "a number inside code never triggers" (fun () ->
      Expect.equal ValueNone (triggerAt("```\n#12", 6)) "an open fence"

      Expect.equal ValueNone (triggerAt("```\n#12\n```", 11)) "a closed fence"

      Expect.equal ValueNone (triggerAt("`#12`", 5)) "an inline span")

    testCase "a color never triggers, a plain number does" (fun () ->
      Expect.equal ValueNone (triggerAt("#000", 4)) "black"

      Expect.equal ValueNone (triggerAt("#ff8a4c", 7)) "six hex digits"

      Expect.equal
        ValueNone
        (triggerAt("#0f3", 4))
        "three digits with a letter"

      Expect.equal
        (ValueSome(IssueRef(0, "123")))
        (triggerAt("#123", 4))
        "a plain number")

    testCase
      "a mention completes with its spacing and the caret past it"
      (fun () ->
        Expect.equal
          ("hi @kai ", 8)
          (mentionCompleted("hi @ka", Mention(3, "ka"), "kai"))
          "one space follows")

    testCase "a mention keeps the spacing already there" (fun () ->
      Expect.equal
        ("@kai you", 5)
        (mentionCompleted("@ka you", Mention(0, "ka"), "kai"))
        "no second space")

    testCase
      "an issue completes to its address with the caret right after"
      (fun () ->
        Expect.equal
          ("see https://x.test/1 ", 20)
          (issueCompleted("see #12", IssueRef(4, "12"), "https://x.test/1"))
          "the caret sits right after the address")

    testCase
      "a trigger the text no longer holds leaves the text alone"
      (fun () ->
        Expect.equal
          ("other", 5)
          (mentionCompleted("other", Mention(0, "ka"), "kai"))
          "stale")
  ]
