#include "githubclient.h"

#include <QJsonArray>
#include <QJsonDocument>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QUrl>

namespace {

constexpr const char *kValidatedFile = "validated.json";
constexpr const char *kRejectedFile = "rejected.json";

// Whether the hash appears anywhere in the list object.
bool containsHash(const QJsonObject &obj, const QString &hash)
{
    for (auto it = obj.begin(); it != obj.end(); ++it)
    {
        const QJsonArray arr = it.value().toArray();
        for (const QJsonValue &v : arr)
        {
            if (v.toString().compare(hash, Qt::CaseInsensitive) == 0)
                return true;
        }
    }
    return false;
}

// Adds the hash under `name`, creating the array if needed. No-op if already present.
void addHash(QJsonObject &obj, const QString &name, const QString &hash)
{
    QJsonArray arr = obj.value(name).toArray();
    for (const QJsonValue &v : arr)
    {
        if (v.toString().compare(hash, Qt::CaseInsensitive) == 0)
            return;
    }
    arr.append(hash);
    obj[name] = arr;
}

// Removes the hash from every group; drops a group that becomes empty.
void removeHash(QJsonObject &obj, const QString &hash)
{
    const QStringList keys = obj.keys();
    for (const QString &key : keys)
    {
        const QJsonArray arr = obj.value(key).toArray();
        QJsonArray kept;
        for (const QJsonValue &v : arr)
        {
            if (v.toString().compare(hash, Qt::CaseInsensitive) != 0)
                kept.append(v);
        }
        if (kept.isEmpty())
            obj.remove(key);
        else
            obj[key] = kept;
    }
}

} // namespace

GitHubClient::GitHubClient(QObject *parent)
    : QObject(parent)
    , m_net(new QNetworkAccessManager(this))
{
}

void GitHubClient::setRepo(const QString &ownerRepo)
{
    const int slash = ownerRepo.indexOf('/');
    if (slash > 0)
    {
        m_owner = ownerRepo.left(slash).trimmed();
        m_repo = ownerRepo.mid(slash + 1).trimmed();
    }
    else
    {
        m_owner.clear();
        m_repo.clear();
    }
}

void GitHubClient::applyHeaders(QNetworkRequest &request) const
{
    request.setRawHeader("Authorization", QByteArray("Bearer ") + m_token.toUtf8());
    request.setRawHeader("Accept", "application/vnd.github+json");
    request.setRawHeader("User-Agent", "SGLoader-PatchValidator");
    request.setRawHeader("X-GitHub-Api-Version", "2022-11-28");
}

void GitHubClient::submitBatch(const QList<Entry> &entries, bool approved)
{
    if (m_token.isEmpty() || m_owner.isEmpty() || m_repo.isEmpty())
    {
        emit finished(false, "Set a GitHub token and repository first.");
        return;
    }
    if (entries.isEmpty())
    {
        emit finished(false, "No patches in the list.");
        return;
    }

    fetchFile(kValidatedFile,
        [=](QJsonObject validated, QString validatedSha) {
            fetchFile(kRejectedFile,
                [=](QJsonObject rejected, QString rejectedSha) {
                    QJsonObject validatedObj = validated;
                    QJsonObject rejectedObj = rejected;
                    bool validatedChanged = false;
                    bool rejectedChanged = false;
                    int added = 0, moved = 0, skipped = 0;

                    for (const Entry &entry : entries)
                    {
                        const bool inValidated = containsHash(validatedObj, entry.hash);
                        const bool inRejected = containsHash(rejectedObj, entry.hash);

                        if (approved)
                        {
                            if (inValidated)
                            {
                                emit entryProcessed(entry.hash, "already approved - skipped");
                                ++skipped;
                                continue;
                            }
                            addHash(validatedObj, entry.name, entry.hash);
                            validatedChanged = true;
                            if (inRejected)
                            {
                                removeHash(rejectedObj, entry.hash);
                                rejectedChanged = true;
                                emit entryProcessed(entry.hash, "approved (moved from rejected)");
                                ++moved;
                            }
                            else
                            {
                                emit entryProcessed(entry.hash, "approved");
                                ++added;
                            }
                        }
                        else
                        {
                            if (inRejected)
                            {
                                emit entryProcessed(entry.hash, "already rejected - skipped");
                                ++skipped;
                                continue;
                            }
                            addHash(rejectedObj, entry.name, entry.hash);
                            rejectedChanged = true;
                            if (inValidated)
                            {
                                removeHash(validatedObj, entry.hash);
                                validatedChanged = true;
                                emit entryProcessed(entry.hash, "rejected (moved from approved)");
                                ++moved;
                            }
                            else
                            {
                                emit entryProcessed(entry.hash, "rejected");
                                ++added;
                            }
                        }
                    }

                    const QString summary = QString("%1 added, %2 moved, %3 skipped")
                                                .arg(added).arg(moved).arg(skipped);

                    if (!validatedChanged && !rejectedChanged)
                    {
                        emit finished(true, "Nothing to commit - " + summary);
                        return;
                    }

                    auto pushRejected = [=]() {
                        if (rejectedChanged)
                        {
                            putFile(kRejectedFile, rejectedObj, rejectedSha,
                                    "Patch validator: update rejected list",
                                    [=]() { emit finished(true, "Done - " + summary); },
                                    [=](const QString &e) { emit finished(false, e); });
                        }
                        else
                        {
                            emit finished(true, "Done - " + summary);
                        }
                    };

                    if (validatedChanged)
                    {
                        putFile(kValidatedFile, validatedObj, validatedSha,
                                "Patch validator: update validated list",
                                pushRejected,
                                [=](const QString &e) { emit finished(false, e); });
                    }
                    else
                    {
                        pushRejected();
                    }
                },
                [=](const QString &e) { emit finished(false, e); });
        },
        [=](const QString &e) { emit finished(false, e); });
}

void GitHubClient::fetchFile(const QString &path,
                             const std::function<void(QJsonObject, QString)> &onOk,
                             const std::function<void(QString)> &onErr)
{
    const QString apiUrl = QString("https://api.github.com/repos/%1/%2/contents/%3")
                               .arg(m_owner, m_repo, path);

    QNetworkRequest request{ QUrl(apiUrl + "?ref=" + m_branch) };
    applyHeaders(request);

    emit log("GET " + path);
    QNetworkReply *reply = m_net->get(request);
    connect(reply, &QNetworkReply::finished, this, [=]() {
        reply->deleteLater();
        const int status = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();

        if (status == 200)
        {
            const QJsonObject resp = QJsonDocument::fromJson(reply->readAll()).object();
            const QString sha = resp.value("sha").toString();
            const QByteArray content = QByteArray::fromBase64(
                resp.value("content").toString().toUtf8().replace('\n', ""));
            onOk(QJsonDocument::fromJson(content).object(), sha);
        }
        else if (status == 404)
        {
            onOk(QJsonObject(), QString()); // file does not exist yet - it will be created
        }
        else
        {
            onErr(QString("GET %1 failed: HTTP %2\n%3")
                      .arg(path).arg(status)
                      .arg(QString::fromUtf8(reply->readAll())));
        }
    });
}

void GitHubClient::putFile(const QString &path, const QJsonObject &content, const QString &sha,
                           const QString &message,
                           const std::function<void()> &onOk,
                           const std::function<void(QString)> &onErr)
{
    const QString apiUrl = QString("https://api.github.com/repos/%1/%2/contents/%3")
                               .arg(m_owner, m_repo, path);

    QJsonObject body;
    body["message"] = message;
    body["content"] = QString::fromUtf8(
        QJsonDocument(content).toJson(QJsonDocument::Indented).toBase64());
    body["branch"] = m_branch;
    if (!sha.isEmpty())
        body["sha"] = sha;

    QNetworkRequest request{ QUrl(apiUrl) };
    applyHeaders(request);
    request.setHeader(QNetworkRequest::ContentTypeHeader, "application/json");

    emit log("PUT " + path);
    QNetworkReply *reply = m_net->put(request, QJsonDocument(body).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [=]() {
        reply->deleteLater();
        const int status = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        if (status == 200 || status == 201)
        {
            emit log(path + ": committed.");
            onOk();
        }
        else
        {
            onErr(QString("PUT %1 failed: HTTP %2\n%3")
                      .arg(path).arg(status)
                      .arg(QString::fromUtf8(reply->readAll())));
        }
    });
}
