#pragma once

#include <QJsonObject>
#include <QList>
#include <QObject>
#include <QString>

#include <functional>

class QNetworkAccessManager;
class QNetworkRequest;

// Pushes patch verdicts to a GitHub repository through the Contents REST API.
//
// A whole batch is processed at once: both lists are fetched a single time, every
// entry is checked against them, the new ones are added (entries already present
// are skipped) and the changed files are committed. "Approve" also drops the hash
// from the rejected list and vice versa, so the two lists never disagree.
class GitHubClient : public QObject
{
    Q_OBJECT

public:
    struct Entry
    {
        QString name;
        QString hash;
    };

    explicit GitHubClient(QObject *parent = nullptr);

    void setToken(const QString &token) { m_token = token; }
    void setRepo(const QString &ownerRepo); // "owner/repo"
    void setBranch(const QString &branch) { m_branch = branch; }

    void submitBatch(const QList<Entry> &entries, bool approved);

signals:
    void log(const QString &message);
    void entryProcessed(const QString &hash, const QString &status);
    void finished(bool ok, const QString &message);

private:
    void fetchFile(const QString &path,
                   const std::function<void(QJsonObject, QString)> &onOk,
                   const std::function<void(QString)> &onErr);
    void putFile(const QString &path, const QJsonObject &content, const QString &sha,
                 const QString &message,
                 const std::function<void()> &onOk,
                 const std::function<void(QString)> &onErr);
    void applyHeaders(QNetworkRequest &request) const;

    QNetworkAccessManager *m_net;
    QString m_token;
    QString m_owner;
    QString m_repo;
    QString m_branch { "main" };
};
