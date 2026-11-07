// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// GitHub の API の窓口。bot は URL・ページ送り・応答の形を知らずに gh.postComment() のように呼ぶ。
// 応答は valibot で検証し、形が違えば例外にする。スキーマは bot が読む項目だけを書く。
// looseObject にする: 書いていない項目が増えても失敗せず、捨てもしない。
import fs from 'node:fs';
import * as v from 'valibot';

const ActorSchema = v.looseObject({
  login: v.string(),
  id: v.optional(v.number()),
  type: v.optional(v.string()),
});
const LabelSchema = v.looseObject({ name: v.string() });
const CommentSchema = v.looseObject({
  id: v.number(),
  body: v.nullish(v.string()),
  user: v.nullable(ActorSchema),
  created_at: v.string(),
  updated_at: v.optional(v.string()),
});
const ReviewSchema = v.looseObject({
  id: v.number(),
  body: v.nullish(v.string()),
  user: v.nullable(ActorSchema),
  submitted_at: v.nullish(v.string()),
});
const IssueSchema = v.looseObject({
  number: v.number(),
  title: v.string(),
  state: v.string(),
  body: v.nullish(v.string()),
  user: ActorSchema,
  labels: v.array(LabelSchema),
  assignees: v.nullish(v.array(v.looseObject({ login: v.string() }))),
  created_at: v.string(),
  pull_request: v.optional(v.unknown()),
});
const PullSchema = v.looseObject({
  number: v.number(),
  title: v.string(),
  state: v.string(),
  body: v.nullish(v.string()),
  user: ActorSchema,
  labels: v.array(LabelSchema),
  created_at: v.string(),
  draft: v.optional(v.boolean()),
  node_id: v.string(),
  author_association: v.string(),
  head: v.looseObject({ sha: v.string() }),
  base: v.looseObject({ ref: v.string() }),
});
const CommitPullSchema = v.looseObject({ number: v.number(), title: v.string(), merged_at: v.nullish(v.string()) });
const IssueEventSchema = v.looseObject({
  id: v.number(),
  event: v.string(),
  created_at: v.string(),
  label: v.optional(v.looseObject({ name: v.string() })),
});
const PullFileSchema = v.looseObject({ filename: v.string(), status: v.string(), patch: v.optional(v.string()) });
const AlertSchema = v.looseObject({
  number: v.number(),
  html_url: v.string(),
  rule: v.nullish(v.looseObject({
    id: v.nullish(v.string()),
    severity: v.nullish(v.string()),
    security_severity_level: v.nullish(v.string()),
    description: v.nullish(v.string()),
  })),
  most_recent_instance: v.nullish(v.looseObject({
    location: v.nullish(v.looseObject({ path: v.nullish(v.string()), start_line: v.nullish(v.number()) })),
  })),
});
const ContentFileSchema = v.looseObject({ content: v.string(), sha: v.string() });
const ShaSchema = v.looseObject({ sha: v.string() });
const RefSchema = v.looseObject({ object: ShaSchema });
const PermissionSchema = v.looseObject({ permission: v.string() });
const AssigneesSchema = v.looseObject({ assignees: v.nullish(v.array(v.looseObject({ login: v.string() }))) });
const GraphqlResponseSchema = v.looseObject({
  data: v.nullish(v.unknown()),
  errors: v.nullish(v.array(v.looseObject({ message: v.string() }))),
});
const SponsorsDataSchema = v.looseObject({
  user: v.nullish(v.looseObject({
    sponsorshipsAsMaintainer: v.nullish(v.looseObject({
      pageInfo: v.looseObject({ hasNextPage: v.boolean(), endCursor: v.nullish(v.string()) }),
      nodes: v.array(v.looseObject({
        privacyLevel: v.nullish(v.string()),
        sponsorEntity: v.nullish(v.looseObject({ login: v.optional(v.string()) })),
      })),
    })),
  })),
});

export type Comment = v.InferOutput<typeof CommentSchema>;
export type Issue = v.InferOutput<typeof IssueSchema>;
export type Pull = v.InferOutput<typeof PullSchema>;
export type IssueEvent = v.InferOutput<typeof IssueEventSchema>;
export type PullFile = v.InferOutput<typeof PullFileSchema>;
export type Alert = v.InferOutput<typeof AlertSchema>;
export type CommitPull = v.InferOutput<typeof CommitPullSchema>;

/** HTTP の層。GET の 404 は null を返す。テストではインメモリの実装に差し替える。 */
export type Transport = {
  request(method: string, path: string, body?: unknown, auth?: string): Promise<unknown>;
  /** 一覧の全ページを読む。上限到達や API エラーのときは、欠けた一覧で判断しないように例外にする。 */
  list(path: string): Promise<unknown[]>;
  graphql(query: string, variables: Record<string, unknown>): Promise<unknown>;
};
export type Fetcher = (url: string, init?: RequestInit) => Promise<Pick<Response, 'ok' | 'status' | 'json' | 'text'>>;

export function httpTransport({ token, repository, apiUrl = 'https://api.github.com', fetcher = fetch }: {
  token: string | undefined;
  repository: string | undefined;
  apiUrl?: string;
  fetcher?: Fetcher;
}): Transport {
  const headers = (auth = token) => ({ Authorization: `Bearer ${auth}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' });
  const request = async (method: string, path: string, body?: unknown, auth?: string): Promise<unknown> => {
    const response = await fetcher(`${apiUrl}/repos/${repository}${path}`, {
      method,
      headers: headers(auth),
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (method === 'GET' && response.status === 404) return null;
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? null : response.json();
  };
  const list = async (path: string): Promise<unknown[]> => {
    const result: unknown[] = [];
    for (let page = 1; page <= 100; page++) {
      const batch = await request('GET', `${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
      if (!Array.isArray(batch)) throw new Error(`一覧ではありません: ${path}`);
      result.push(...batch);
      if (batch.length < 100) return result;
    }
    throw new Error(`一覧が大きすぎるため中止: ${path}`);
  };
  const graphql = async (query: string, variables: Record<string, unknown>): Promise<unknown> => {
    const response = await fetcher(`${apiUrl}/graphql`, { method: 'POST', headers: headers(), body: JSON.stringify({ query, variables }) });
    if (!response.ok) throw new Error(`POST /graphql: ${response.status} ${await response.text()}`);
    const result = v.parse(GraphqlResponseSchema, await response.json());
    if (result.errors?.length) throw new Error(`GraphQL: ${result.errors.map(e => e.message).join(', ')}`);
    return result.data;
  };
  return { request, list, graphql };
}

const envTransport = () => httpTransport({
  token: process.env.GITHUB_TOKEN,
  repository: process.env.GITHUB_REPOSITORY,
  apiUrl: process.env.GITHUB_API_URL,
});

export function createGitHub(transport: Transport = envTransport()) {
  const { request, list, graphql } = transport;
  const listOf = async <S extends v.GenericSchema>(schema: S, path: string) => v.parse(v.array(schema), await list(path));
  const encode = encodeURIComponent;

  return {
    // ---- Issue ----
    getIssue: async (number: number) => v.parse(v.nullable(IssueSchema), await request('GET', `/issues/${number}`)),
    closeIssueAsNotPlanned: (number: number) => request('PATCH', `/issues/${number}`, { state: 'closed', state_reason: 'not_planned' }),
    listOpenIssuesWithLabel: (label: string) => listOf(IssueSchema, `/issues?state=open&labels=${encode(label)}`),
    listIssueEvents: (number: number) => listOf(IssueEventSchema, `/issues/${number}/events`),
    /** 担当に入れたあとの Assignees を返す (割り当てられない人は GitHub が黙って外す)。 */
    addAssignees: async (number: number, logins: string[]) => {
      const issue = v.parse(AssigneesSchema, await request('POST', `/issues/${number}/assignees`, { assignees: logins }));
      return issue.assignees ?? [];
    },

    // ---- コメント ----
    listComments: (number: number) => listOf(CommentSchema, `/issues/${number}/comments`),
    postComment: (number: number, body: string) => request('POST', `/issues/${number}/comments`, { body }),
    updateComment: (commentId: number, body: string) => request('PATCH', `/issues/comments/${commentId}`, { body }),
    /** bot が書いた、marker を含むコメント。 */
    findBotComment: async (number: number, marker: string) => {
      const comments = await listOf(CommentSchema, `/issues/${number}/comments`);
      return comments.find(c => c.user?.type === 'Bot' && c.body?.includes(marker)) ?? null;
    },
    reactToComment: (commentId: number, content: string) => request('POST', `/issues/comments/${commentId}/reactions`, { content }),

    // ---- ラベル ----
    listLabels: () => listOf(LabelSchema, '/labels'),
    getLabel: async (name: string) => v.parse(v.nullable(LabelSchema), await request('GET', `/labels/${encode(name)}`)),
    createLabel: (name: string, color: string, description: string) => request('POST', '/labels', { name, color, description }),
    addLabels: (number: number, labels: string[]) => request('POST', `/issues/${number}/labels`, { labels }),
    removeLabel: (number: number, label: string) => request('DELETE', `/issues/${number}/labels/${encode(label)}`),

    // ---- Pull Request ----
    getPull: async (number: number) => v.parse(v.nullable(PullSchema), await request('GET', `/pulls/${number}`)),
    listOpenPulls: () => listOf(PullSchema, '/pulls?state=open'),
    listPullFiles: (number: number) => listOf(PullFileSchema, `/pulls/${number}/files`),
    listPullReviewComments: (number: number) => listOf(CommentSchema, `/pulls/${number}/comments`),
    listPullReviews: (number: number) => listOf(ReviewSchema, `/pulls/${number}/reviews`),
    listPullsOfCommit: (sha: string) => listOf(CommitPullSchema, `/commits/${sha}/pulls`),
    convertPullToDraft: (nodeId: string) => graphql('mutation($id: ID!) { convertPullRequestToDraft(input: { pullRequestId: $id }) { pullRequest { isDraft } } }', { id: nodeId }),

    // ---- リポジトリの中身 ----
    getCollaboratorPermission: async (login: string) => v.parse(v.nullable(PermissionSchema), await request('GET', `/collaborators/${login}/permission`))?.permission ?? null,
    /** ファイルの中身と sha (無ければ null)。 */
    getFile: async (path: string, ref: string) => {
      const file = v.parse(v.nullable(ContentFileSchema), await request('GET', `/contents/${path}?ref=${ref}`));
      return file && { text: Buffer.from(file.content, 'base64').toString('utf8'), sha: file.sha };
    },
    putFile: (path: string, file: { message: string; text: string; branch: string; sha: string | undefined }) => request('PUT', `/contents/${path}`, {
      message: file.message,
      content: Buffer.from(file.text).toString('base64'),
      branch: file.branch,
      sha: file.sha,
    }),
    branchExists: async (branch: string) => await request('GET', `/branches/${branch}`) !== null,
    /** ref (heads/main・tags/v1.0.0 など) の指す commit (無ければ null)。 */
    getRefSha: async (ref: string) => v.parse(v.nullable(RefSchema), await request('GET', `/git/ref/${ref}`))?.object.sha ?? null,
    createRef: (ref: string, sha: string) => request('POST', '/git/refs', { ref, sha }),
    /** 履歴の無い commit を、1 つのファイルだけで作る。 */
    createRootCommit: async (message: string, path: string, content: string) => {
      const tree = v.parse(ShaSchema, await request('POST', '/git/trees', { tree: [{ path, mode: '100644', type: 'blob', content }] }));
      return v.parse(ShaSchema, await request('POST', '/git/commits', { message, tree: tree.sha, parents: [] })).sha;
    },
    createDraftRelease: (release: { tag: string; name: string; prerelease: boolean; body: string }) => request('POST', '/releases', {
      tag_name: release.tag, name: release.name, draft: true, prerelease: release.prerelease, body: release.body,
    }),
    dispatchWorkflow: (workflow: string, ref: string, auth?: string) => request('POST', `/actions/workflows/${workflow}/dispatches`, { ref }, auth),
    setCommitStatus: (sha: string, status: { state: 'success' | 'failure'; context: string; description: string }) => request('POST', `/statuses/${sha}`, status),

    // ---- Code scanning ----
    /** ref の開いている指摘。解析がまだ無い ref は GitHub が 404 を返すので、指摘なしとして扱う。 */
    listOpenAlerts: async (tool: string, ref: string) => {
      const alerts: Alert[] = [];
      for (let page = 1; page <= 100; page++) {
        const batch = v.parse(v.nullable(v.array(AlertSchema)), await request('GET', `/code-scanning/alerts?tool_name=${tool}&state=open&ref=${encode(ref)}&per_page=100&page=${page}`)) ?? [];
        alerts.push(...batch);
        if (batch.length < 100) return alerts;
      }
      throw new Error(`一覧が大きすぎるため中止: ${tool} ${ref}`);
    },

    // ---- Sponsors ----
    /** 名前を公開にした人の login (古い順。同じ人は 1 回だけ)。 */
    listPublicSponsors: async (login: string) => {
      const query = `query($login: String!, $after: String) {
        user(login: $login) {
          sponsorshipsAsMaintainer(first: 100, after: $after, activeOnly: false, includePrivate: false, orderBy: {field: CREATED_AT, direction: ASC}) {
            pageInfo { hasNextPage endCursor }
            nodes { privacyLevel sponsorEntity { ... on User { login } ... on Organization { login } } }
          }
        }
      }`;
      const logins: string[] = [];
      let after: string | null | undefined = null;
      for (let page = 0; page < 20; page++) {
        const data: v.InferOutput<typeof SponsorsDataSchema> = v.parse(SponsorsDataSchema, await graphql(query, { login, after }));
        const sponsorships = data.user?.sponsorshipsAsMaintainer;
        if (!sponsorships) break;
        for (const node of sponsorships.nodes) {
          const sponsor = node.sponsorEntity?.login;
          if (node.privacyLevel === 'PUBLIC' && sponsor && !logins.includes(sponsor)) logins.push(sponsor);
        }
        if (!sponsorships.pageInfo.hasNextPage) break;
        after = sponsorships.pageInfo.endCursor;
      }
      return logins;
    },
  };
}
export type GitHub = ReturnType<typeof createGitHub>;

/** GitHub Actions が渡すイベントの JSON を読む。 */
export function readEvent<S extends v.GenericSchema>(schema: S): v.InferOutput<S> {
  const file = process.env.GITHUB_EVENT_PATH;
  if (!file) throw new Error('GITHUB_EVENT_PATH がありません');
  return v.parse(schema, JSON.parse(fs.readFileSync(file, 'utf8')));
}
